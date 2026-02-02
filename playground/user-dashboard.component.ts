import { Component, OnInit, OnDestroy, Input, Output, EventEmitter, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { Subject, BehaviorSubject, Observable, combineLatest, timer, Subscription } from 'rxjs';
import { takeUntil, debounceTime, distinctUntilChanged, switchMap, map, filter, catchError } from 'rxjs/operators';
import { UserService } from '../services/user.service';
import { NotificationService } from '../services/notification.service';
import { AnalyticsService } from '../services/analytics.service';
import { User, UserPreferences, DashboardMetrics, ActivityLog, NotificationType } from '../models';

interface DashboardState {
  isLoading: boolean;
  hasError: boolean;
  errorMessage: string | null;
  lastUpdated: Date | null;
}

type ViewMode = 'grid' | 'list' | 'compact';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-user-dashboard',
  templateUrl: './user-dashboard.component.html',
  styleUrls: ['./user-dashboard.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserDashboardComponent implements OnInit, OnDestroy {
  @Input() userId: string = '';
  @Input() enableRealTimeUpdates: boolean = true;
  @Input() refreshInterval: number = 30000;
  @Input() maxActivityItems: number = 50;

  @Output() userUpdated = new EventEmitter<User>();
  @Output() preferencesChanged = new EventEmitter<UserPreferences>();
  @Output() errorOccurred = new EventEmitter<Error>();
  @Output() dashboardClosed = new EventEmitter<void>();

  private destroy$ = new Subject<void>();
  private searchSubject$ = new BehaviorSubject<string>('');
  private metricsCache = new Map<string, DashboardMetrics>();
  private refreshSubscription: Subscription | null = null;
  private websocketConnection: WebSocket | null = null;

  currentUser: User | null = null;
  userPreferences: UserPreferences | null = null;
  dashboardMetrics: DashboardMetrics | null = null;
  activityLogs: ActivityLog[] = [];
  filteredActivityLogs: ActivityLog[] = [];

  dashboardState: DashboardState = {
    isLoading: false,
    hasError: false,
    errorMessage: null,
    lastUpdated: null
  };

  viewMode: ViewMode = 'grid';
  sortDirection: SortDirection = 'desc';
  currentPage: number = 1;
  pageSize: number = 10;
  totalPages: number = 1;
  searchQuery: string = '';
  selectedCategories: string[] = [];
  isExpanded: boolean = false;
  isDarkMode: boolean = false;
  notificationCount: number = 0;

  readonly availableCategories = ['login', 'logout', 'settings', 'profile', 'security', 'billing'];
  readonly pageSizeOptions = [5, 10, 25, 50, 100];

  constructor(
    private userService: UserService,
    private notificationService: NotificationService,
    private analyticsService: AnalyticsService,
    private changeDetectorRef: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.initializeDashboard();
    this.setupSearchSubscription();
    this.loadUserData();
    if (this.enableRealTimeUpdates) {
      this.startRealTimeUpdates();
    }
    this.trackPageView();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.stopRealTimeUpdates();
    this.closeWebSocketConnection();
    this.metricsCache.clear();
  }

  private initializeDashboard(): void {
    this.dashboardState = {
      isLoading: true,
      hasError: false,
      errorMessage: null,
      lastUpdated: null
    };
    this.loadUserPreferences();
  }

  private setupSearchSubscription(): void {
    this.searchSubject$.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(query => {
      this.filterActivityLogs(query);
      this.changeDetectorRef.markForCheck();
    });
  }

  async loadUserData(): Promise<void> {
    if (!this.userId) {
      this.handleError(new Error('User ID is required'));
      return;
    }

    this.dashboardState.isLoading = true;
    this.changeDetectorRef.markForCheck();

    try {
      const [user, metrics, activities] = await Promise.all([
        this.userService.getUserById(this.userId).toPromise(),
        this.userService.getUserMetrics(this.userId).toPromise(),
        this.userService.getUserActivityLogs(this.userId, this.maxActivityItems).toPromise()
      ]);

      this.currentUser = user ?? null;
      this.dashboardMetrics = metrics ?? null;
      this.activityLogs = activities ?? [];
      this.filteredActivityLogs = [...this.activityLogs];
      this.calculateTotalPages();

      if (metrics) {
        this.metricsCache.set(this.userId, metrics);
      }

      this.dashboardState = {
        isLoading: false,
        hasError: false,
        errorMessage: null,
        lastUpdated: new Date()
      };

      this.userUpdated.emit(this.currentUser!);
    } catch (error) {
      this.handleError(error as Error);
    }

    this.changeDetectorRef.markForCheck();
  }

  private loadUserPreferences(): void {
    const storedPreferences = localStorage.getItem(`user_prefs_${this.userId}`);
    if (storedPreferences) {
      try {
        this.userPreferences = JSON.parse(storedPreferences);
        this.applyPreferences();
      } catch (e) {
        this.userPreferences = this.getDefaultPreferences();
      }
    } else {
      this.userPreferences = this.getDefaultPreferences();
    }
  }

  private getDefaultPreferences(): UserPreferences {
    return {
      theme: 'light',
      language: 'en',
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      notifications: {
        email: true,
        push: true,
        sms: false
      },
      displayDensity: 'comfortable'
    };
  }

  private applyPreferences(): void {
    if (!this.userPreferences) return;

    this.isDarkMode = this.userPreferences.theme === 'dark';
    switch (this.userPreferences.displayDensity) {
      case 'compact':
        this.pageSize = 25;
        break;
      case 'comfortable':
        this.pageSize = 10;
        break;
      case 'spacious':
        this.pageSize = 5;
        break;
      default:
        this.pageSize = 10;
    }
  }

  saveUserPreferences(preferences: Partial<UserPreferences>): void {
    if (!this.userPreferences) return;

    this.userPreferences = { ...this.userPreferences, ...preferences };
    localStorage.setItem(`user_prefs_${this.userId}`, JSON.stringify(this.userPreferences));
    this.applyPreferences();
    this.preferencesChanged.emit(this.userPreferences);
    this.notificationService.showSuccess('Preferences saved successfully');
  }

  private startRealTimeUpdates(): void {
    this.refreshSubscription = timer(this.refreshInterval, this.refreshInterval)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.refreshDashboardData());

    this.initializeWebSocket();
  }

  private stopRealTimeUpdates(): void {
    if (this.refreshSubscription) {
      this.refreshSubscription.unsubscribe();
      this.refreshSubscription = null;
    }
  }

  private initializeWebSocket(): void {
    if (this.websocketConnection) {
      this.closeWebSocketConnection();
    }

    const wsUrl = `wss://api.example.com/ws/user/${this.userId}`;
    this.websocketConnection = new WebSocket(wsUrl);

    this.websocketConnection.onmessage = (event) => {
      this.handleWebSocketMessage(event.data);
    };

    this.websocketConnection.onerror = (error) => {
      console.error('WebSocket error:', error);
      this.scheduleReconnect();
    };

    this.websocketConnection.onclose = () => {
      if (!this.destroy$.closed) {
        this.scheduleReconnect();
      }
    };
  }

  private handleWebSocketMessage(data: string): void {
    try {
      const message = JSON.parse(data);
      switch (message.type) {
        case 'METRICS_UPDATE':
          this.updateMetrics(message.payload);
          break;
        case 'NEW_ACTIVITY':
          this.addActivityLog(message.payload);
          break;
        case 'NOTIFICATION':
          this.handleNotification(message.payload);
          break;
        case 'USER_UPDATE':
          this.updateUserData(message.payload);
          break;
        default:
          console.warn('Unknown message type:', message.type);
      }
    } catch (e) {
      console.error('Failed to parse WebSocket message:', e);
    }
  }

  private scheduleReconnect(): void {
    setTimeout(() => {
      if (!this.destroy$.closed && this.enableRealTimeUpdates) {
        this.initializeWebSocket();
      }
    }, 5000);
  }

  private closeWebSocketConnection(): void {
    if (this.websocketConnection) {
      this.websocketConnection.close();
      this.websocketConnection = null;
    }
  }

  private updateMetrics(metrics: Partial<DashboardMetrics>): void {
    if (this.dashboardMetrics) {
      this.dashboardMetrics = { ...this.dashboardMetrics, ...metrics };
      this.metricsCache.set(this.userId, this.dashboardMetrics);
      this.changeDetectorRef.markForCheck();
    }
  }

  private addActivityLog(activity: ActivityLog): void {
    this.activityLogs = [activity, ...this.activityLogs].slice(0, this.maxActivityItems);
    this.filterActivityLogs(this.searchQuery);
    this.changeDetectorRef.markForCheck();
  }

  private handleNotification(notification: { type: NotificationType; message: string }): void {
    this.notificationCount++;
    switch (notification.type) {
      case 'success':
        this.notificationService.showSuccess(notification.message);
        break;
      case 'warning':
        this.notificationService.showWarning(notification.message);
        break;
      case 'error':
        this.notificationService.showError(notification.message);
        break;
      case 'info':
      default:
        this.notificationService.showInfo(notification.message);
    }
    this.changeDetectorRef.markForCheck();
  }

  private updateUserData(userData: Partial<User>): void {
    if (this.currentUser) {
      this.currentUser = { ...this.currentUser, ...userData };
      this.userUpdated.emit(this.currentUser);
      this.changeDetectorRef.markForCheck();
    }
  }

  async refreshDashboardData(): Promise<void> {
    if (this.dashboardState.isLoading) return;

    try {
      const cachedMetrics = this.metricsCache.get(this.userId);
      if (cachedMetrics && this.isCacheValid(cachedMetrics)) {
        this.dashboardMetrics = cachedMetrics;
      } else {
        await this.loadUserData();
      }
    } catch (error) {
      console.error('Failed to refresh dashboard:', error);
    }
  }

  private isCacheValid(metrics: DashboardMetrics): boolean {
    if (!metrics.timestamp) return false;
    const cacheAge = Date.now() - new Date(metrics.timestamp).getTime();
    return cacheAge < this.refreshInterval;
  }

  filterActivityLogs(query: string): void {
    this.searchQuery = query;
    if (!query.trim()) {
      this.filteredActivityLogs = this.applyFiltersAndSort(this.activityLogs);
    } else {
      const lowerQuery = query.toLowerCase();
      this.filteredActivityLogs = this.applyFiltersAndSort(
        this.activityLogs.filter(log =>
          log.action.toLowerCase().includes(lowerQuery) ||
          log.description.toLowerCase().includes(lowerQuery) ||
          log.category.toLowerCase().includes(lowerQuery)
        )
      );
    }
    this.calculateTotalPages();
    this.currentPage = 1;
  }

  private applyFiltersAndSort(logs: ActivityLog[]): ActivityLog[] {
    let result = [...logs];

    if (this.selectedCategories.length > 0) {
      result = result.filter(log => this.selectedCategories.includes(log.category));
    }

    result.sort((a, b) => {
      const dateA = new Date(a.timestamp).getTime();
      const dateB = new Date(b.timestamp).getTime();
      return this.sortDirection === 'desc' ? dateB - dateA : dateA - dateB;
    });

    return result;
  }

  onSearchChange(query: string): void {
    this.searchSubject$.next(query);
  }

  toggleCategory(category: string): void {
    const index = this.selectedCategories.indexOf(category);
    if (index === -1) {
      this.selectedCategories = [...this.selectedCategories, category];
    } else {
      this.selectedCategories = this.selectedCategories.filter(c => c !== category);
    }
    this.filterActivityLogs(this.searchQuery);
    this.trackFilterChange('category', category);
  }

  clearCategoryFilters(): void {
    this.selectedCategories = [];
    this.filterActivityLogs(this.searchQuery);
  }

  toggleSortDirection(): void {
    this.sortDirection = this.sortDirection === 'desc' ? 'asc' : 'desc';
    this.filterActivityLogs(this.searchQuery);
    this.trackFilterChange('sort', this.sortDirection);
  }

  setViewMode(mode: ViewMode): void {
    this.viewMode = mode;
    this.analyticsService.trackEvent('dashboard_view_mode_changed', { mode });
  }

  private calculateTotalPages(): void {
    this.totalPages = Math.ceil(this.filteredActivityLogs.length / this.pageSize);
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
      this.changeDetectorRef.markForCheck();
    }
  }

  nextPage(): void {
    this.goToPage(this.currentPage + 1);
  }

  previousPage(): void {
    this.goToPage(this.currentPage - 1);
  }

  setPageSize(size: number): void {
    this.pageSize = size;
    this.calculateTotalPages();
    this.currentPage = 1;
    this.changeDetectorRef.markForCheck();
  }

  get paginatedActivityLogs(): ActivityLog[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredActivityLogs.slice(start, start + this.pageSize);
  }

  get hasNextPage(): boolean {
    return this.currentPage < this.totalPages;
  }

  get hasPreviousPage(): boolean {
    return this.currentPage > 1;
  }

  get isLoadingOrError(): boolean {
    return this.dashboardState.isLoading || this.dashboardState.hasError;
  }

  get formattedLastUpdated(): string {
    if (!this.dashboardState.lastUpdated) return 'Never';
    return new Intl.DateTimeFormat('en-US', {
      dateStyle: 'medium',
      timeStyle: 'short'
    }).format(this.dashboardState.lastUpdated);
  }

  toggleExpanded(): void {
    this.isExpanded = !this.isExpanded;
    this.analyticsService.trackEvent('dashboard_expanded_toggled', { isExpanded: this.isExpanded });
  }

  async exportActivityLogs(format: 'csv' | 'json' | 'pdf'): Promise<void> {
    this.dashboardState.isLoading = true;
    this.changeDetectorRef.markForCheck();

    try {
      let exportData: Blob;
      let fileName: string;

      switch (format) {
        case 'csv':
          exportData = this.generateCsvExport();
          fileName = `activity_logs_${this.userId}_${Date.now()}.csv`;
          break;
        case 'json':
          exportData = this.generateJsonExport();
          fileName = `activity_logs_${this.userId}_${Date.now()}.json`;
          break;
        case 'pdf':
          exportData = await this.generatePdfExport();
          fileName = `activity_logs_${this.userId}_${Date.now()}.pdf`;
          break;
        default:
          throw new Error('Unsupported export format');
      }

      this.downloadFile(exportData, fileName);
      this.notificationService.showSuccess(`Activity logs exported as ${format.toUpperCase()}`);
      this.analyticsService.trackEvent('activity_logs_exported', { format, count: this.filteredActivityLogs.length });
    } catch (error) {
      this.handleError(error as Error);
    } finally {
      this.dashboardState.isLoading = false;
      this.changeDetectorRef.markForCheck();
    }
  }

  private generateCsvExport(): Blob {
    const headers = ['Timestamp', 'Action', 'Category', 'Description', 'IP Address'];
    const rows = this.filteredActivityLogs.map(log => [
      new Date(log.timestamp).toISOString(),
      log.action,
      log.category,
      `"${log.description.replace(/"/g, '""')}"`,
      log.ipAddress || 'N/A'
    ]);

    const csvContent = [headers.join(','), ...rows.map(row => row.join(','))].join('\n');
    return new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  }

  private generateJsonExport(): Blob {
    const exportData = {
      exportedAt: new Date().toISOString(),
      userId: this.userId,
      totalRecords: this.filteredActivityLogs.length,
      filters: {
        searchQuery: this.searchQuery,
        categories: this.selectedCategories,
        sortDirection: this.sortDirection
      },
      data: this.filteredActivityLogs
    };
    return new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
  }

  private async generatePdfExport(): Promise<Blob> {
    const response = await this.userService.generateActivityLogsPdf(this.userId, this.filteredActivityLogs).toPromise();
    return response ?? new Blob();
  }

  private downloadFile(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  private handleError(error: Error): void {
    console.error('Dashboard error:', error);
    this.dashboardState = {
      isLoading: false,
      hasError: true,
      errorMessage: error.message || 'An unexpected error occurred',
      lastUpdated: this.dashboardState.lastUpdated
    };
    this.errorOccurred.emit(error);
    this.notificationService.showError(error.message);
    this.changeDetectorRef.markForCheck();
  }

  retryLoadData(): void {
    this.dashboardState.hasError = false;
    this.dashboardState.errorMessage = null;
    this.loadUserData();
  }

  private trackPageView(): void {
    this.analyticsService.trackPageView('user_dashboard', {
      userId: this.userId,
      enableRealTimeUpdates: this.enableRealTimeUpdates
    });
  }

  private trackFilterChange(filterType: string, value: string): void {
    this.analyticsService.trackEvent('dashboard_filter_changed', {
      filterType,
      value,
      userId: this.userId
    });
  }

  closeDashboard(): void {
    this.dashboardClosed.emit();
    this.ngOnDestroy();
  }

  formatMetricValue(value: number, type: 'currency' | 'percentage' | 'number'): string {
    switch (type) {
      case 'currency':
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
      case 'percentage':
        return `${(value * 100).toFixed(1)}%`;
      case 'number':
      default:
        return new Intl.NumberFormat('en-US').format(value);
    }
  }

  getActivityIcon(category: string): string {
    const iconMap: Record<string, string> = {
      login: 'login',
      logout: 'logout',
      settings: 'settings',
      profile: 'person',
      security: 'security',
      billing: 'credit_card'
    };
    return iconMap[category] || 'info';
  }

  getActivityColorClass(category: string): string {
    const colorMap: Record<string, string> = {
      login: 'text-green-500',
      logout: 'text-gray-500',
      settings: 'text-blue-500',
      profile: 'text-purple-500',
      security: 'text-red-500',
      billing: 'text-yellow-500'
    };
    return colorMap[category] || 'text-gray-400';
  }

  calculateMetricTrend(current: number, previous: number): { value: number; direction: 'up' | 'down' | 'neutral' } {
    if (previous === 0) {
      return { value: 0, direction: 'neutral' };
    }
    const percentChange = ((current - previous) / previous) * 100;
    if (Math.abs(percentChange) < 0.1) {
      return { value: 0, direction: 'neutral' };
    }
    return {
      value: Math.abs(percentChange),
      direction: percentChange > 0 ? 'up' : 'down'
    };
  }

  isActivityRecent(timestamp: string | Date): boolean {
    const activityTime = new Date(timestamp).getTime();
    const oneHourAgo = Date.now() - 60 * 60 * 1000;
    return activityTime > oneHourAgo;
  }

  formatRelativeTime(timestamp: string | Date): string {
    const date = new Date(timestamp);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffSec = Math.floor(diffMs / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHour = Math.floor(diffMin / 60);
    const diffDay = Math.floor(diffHour / 24);

    if (diffSec < 60) return 'Just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    if (diffHour < 24) return `${diffHour}h ago`;
    if (diffDay < 7) return `${diffDay}d ago`;

    return date.toLocaleDateString();
  }
}
