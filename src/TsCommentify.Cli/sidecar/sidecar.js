#!/usr/bin/env node
'use strict';

// TsCommentify Node/TS-AST sidecar (PoC).
//
// Mirrors the SurfaceQ.Sidecar.Node transport exactly: newline-delimited
// JSON-RPC over stdio. The .NET host (SidecarClient) writes one JSON request
// per line and reads one JSON response per line. Unlike the regex parser, this
// uses the real TypeScript compiler, so it sees whole declarations (not single
// physical lines) and detects existing comments via getLeadingCommentRanges.
//
// Methods:
//   ping  -> "pong"
//   parse {file} -> { declarations: [{ kind, name, line, params, returnType, hasComment }], errors }
//
// The `typescript` module is resolved from TSCOMMENTIFY_TS if set (so a host can
// point at a bundled copy), else from the normal node_modules resolution.

const fs = require('fs');
const readline = require('readline');
const ts = require(process.env.TSCOMMENTIFY_TS || 'typescript');

const rl = readline.createInterface({ input: process.stdin });

rl.on('line', (line) => {
  let msg;
  try {
    msg = JSON.parse(line);
  } catch (e) {
    return;
  }
  if (!msg || typeof msg !== 'object') {
    return;
  }
  if (msg.method === 'ping') {
    respond(msg.id, 'pong');
    return;
  }
  if (msg.method === 'parse') {
    respond(msg.id, parse(msg.params && msg.params.file));
    return;
  }
});

function respond(id, result) {
  process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: id, result: result }) + '\n');
}

function parse(file) {
  const declarations = [];
  const errors = [];
  let content;
  try {
    content = fs.readFileSync(file, 'utf8');
  } catch (e) {
    return { declarations, errors: [{ line: 1, message: 'cannot read file' }] };
  }
  const sf = ts.createSourceFile(file, content, ts.ScriptTarget.Latest, true);
  const diags = sf.parseDiagnostics || [];
  if (diags.length > 0) {
    // A real parser knows when a file is syntactically broken and can REFUSE to
    // edit it, instead of silently mis-inserting comments into invalid source.
    for (const d of diags) {
      const at = (d.file && typeof d.start === 'number')
        ? d.file.getLineAndCharacterOfPosition(d.start).line + 1 : 1;
      errors.push({ line: at, message: ts.flattenDiagnosticMessageText(d.messageText, '\n') });
    }
    return { declarations, errors };
  }

  // 1-based line where the comment should be inserted: the line of the
  // declaration's first real token (after any leading trivia).
  //
  // Lines are counted by '\n' only (matching the .NET FileProcessor, which splits
  // the file on '\n' and trims a trailing '\r'). Using TypeScript's own line map
  // would also break on '\r' and U+2028/2029, desyncing the insertion index from
  // the host's line array on files with lone-CR separators.
  const lineOf = (node) => {
    const pos = node.getStart(sf);
    let line = 1;
    for (let i = 0; i < pos; i++) {
      if (sf.text.charCodeAt(i) === 10 /* \n */) line++;
    }
    return line;
  };

  // True iff a real comment documents the node. This is the AST-correct
  // replacement for the regex parser's "nearest non-blank line starts with //,
  // /* or *" heuristic — it never trips on a `* b;` continuation line and never
  // misses a real /** */ block.
  //
  // getFullStart() only sees trivia ABOVE the first decorator/modifier, so for a
  // decorated declaration (e.g. an Angular `@Component()` class) we ALSO probe the
  // gap after each decorator/modifier — the conventional hand-written JSDoc sits
  // between the decorator and the `class`/method keyword. Without this the tool
  // would fail to detect that existing comment and stack a duplicate above it.
  const hasComment = (node) => {
    if ((ts.getLeadingCommentRanges(sf.text, node.getFullStart()) || []).length > 0) {
      return true;
    }
    for (const m of (node.modifiers || [])) {
      if ((ts.getLeadingCommentRanges(sf.text, m.end) || []).length > 0) {
        return true;
      }
    }
    return false;
  };

  // A computed member name (`[Foo.Bar]`, `[Symbol.iterator]`) has no readable
  // identifier to document; getText returns the bracketed expression verbatim.
  const isDocumentableName = (m) => m.name && !ts.isComputedPropertyName(m.name);

  const params = (node) => node.parameters.map((p) => ({
    name: p.name.getText(sf),
    type: p.type ? collapse(p.type.getText(sf)) : null,
  }));

  const ret = (node) => (node.type ? collapse(node.type.getText(sf)) : null);

  // A member only earns a comment when it begins its own physical line: the
  // comment is inserted on the line above, so a member sharing a line (e.g. every
  // member of a single-line `enum E { A, B }`, or an inline `interface A { id }`)
  // cannot be documented without corrupting the source. Top-level declarations are
  // not subject to this — they effectively always begin their line.
  const startsOwnLine = (node) => {
    for (let i = node.getStart(sf) - 1; i >= 0; i--) {
      const ch = sf.text.charCodeAt(i);
      if (ch === 10 /* \n */) return true;
      if (ch !== 32 /* space */ && ch !== 9 /* tab */ && ch !== 13 /* \r */) return false;
    }
    return true;
  };

  // Top-level declaration (function/interface/type/enum/class): always recorded.
  // `sig` is the node carrying the parameter list + return type (the declaration
  // itself for functions, absent for named-type declarations).
  const pushDecl = (kind, name, node, sig) => declarations.push({
    kind, name, line: lineOf(node),
    params: sig ? params(sig) : [], returnType: sig ? ret(sig) : null,
    hasComment: hasComment(node),
  });

  // Member of an interface/class/enum: recorded only when it starts its own line.
  const pushMember = (kind, name, node, withSig) => {
    if (!startsOwnLine(node)) return;
    declarations.push({
      kind, name, line: lineOf(node),
      params: withSig ? params(node) : [], returnType: withSig ? ret(node) : null,
      hasComment: hasComment(node),
    });
  };

  const visit = (node) => {
    if (ts.isFunctionDeclaration(node) && node.name) {
      pushDecl('function', node.name.text, node, node);
    } else if (ts.isVariableStatement(node)) {
      for (const d of node.declarationList.declarations) {
        const init = d.initializer;
        if (init && (ts.isArrowFunction(init) || ts.isFunctionExpression(init)) && ts.isIdentifier(d.name)) {
          // The arrow/function-expression carries the params + return type;
          // the comment is inserted above the `const` statement.
          declarations.push({
            kind: 'function', name: d.name.text, line: lineOf(node),
            params: params(init), returnType: ret(init), hasComment: hasComment(node),
          });
        }
      }
    } else if (ts.isInterfaceDeclaration(node)) {
      pushDecl('interface', node.name.text, node);
      for (const m of node.members) {
        if (!isDocumentableName(m)) continue;
        const mn = unquote(m.name.getText(sf));
        if (ts.isMethodSignature(m)) pushMember('method', mn, m, true);
        else if (ts.isPropertySignature(m)) pushMember('property', mn, m, false);
      }
    } else if (ts.isTypeAliasDeclaration(node)) {
      pushDecl('type', node.name.text, node);
    } else if (ts.isEnumDeclaration(node)) {
      pushDecl('enum', node.name.text, node);
      for (const m of node.members) {
        if (!isDocumentableName(m)) continue;
        pushMember('enum-member', unquote(m.name.getText(sf)), m, false);
      }
    } else if (ts.isClassDeclaration(node)) {
      // An anonymous `export default class {}` has no name to document; its
      // methods are still visited below.
      if (node.name) pushDecl('class', node.name.text, node);
      for (const m of node.members) {
        if (!isDocumentableName(m)) continue;
        const mn = m.name.getText(sf);
        // Only concrete members (with a body) are documented: abstract methods and
        // overload signatures have no body, and documenting each overload signature
        // (rather than the implementation) would stack redundant comments. This also
        // keeps parity with the regex parser, which only matches `{`-bodied methods.
        if (ts.isMethodDeclaration(m) && m.body) pushMember('method', mn, m, true);
        else if ((ts.isGetAccessor(m) || ts.isSetAccessor(m)) && m.body) pushMember('method', mn, m, true);
      }
    } else if (ts.isModuleDeclaration(node) && node.body) {
      // namespace N { ... } / module M { ... }: descend into the body so nested
      // declarations are documented like top-level ones (matching the regex parser,
      // which scans every physical line regardless of nesting). node.body is a
      // ModuleBlock, or — for a dotted name like `namespace A.B {}` — another
      // ModuleDeclaration; forEachChild descends correctly through either.
      ts.forEachChild(node.body, visit);
    }
  };

  ts.forEachChild(sf, visit);
  declarations.sort((a, b) => a.line - b.line);
  return { declarations, errors };
}

function collapse(text) {
  return text.replace(/\s+/g, ' ').trim();
}

// String-literal member names (`enum E { 'a-b' = 1 }`, `interface I { 'x': T }`)
// arrive from getText with their quotes; strip them so the generated description
// reads from the bare name.
function unquote(text) {
  if (text && text.length >= 2) {
    const a = text[0];
    const b = text[text.length - 1];
    if ((a === '"' || a === "'" || a === '`') && a === b) {
      return text.slice(1, -1);
    }
  }
  return text;
}
