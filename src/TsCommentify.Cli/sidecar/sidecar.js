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

  // True iff a real comment immediately precedes the node. This is the
  // AST-correct replacement for the regex parser's "nearest non-blank line
  // starts with //, /* or *" heuristic — it never trips on a `* b;`
  // continuation line and never misses a real /** */ block.
  const hasComment = (node) => {
    const ranges = ts.getLeadingCommentRanges(sf.text, node.getFullStart()) || [];
    return ranges.length > 0;
  };

  const params = (node) => node.parameters.map((p) => ({
    name: p.name.getText(sf),
    type: p.type ? collapse(p.type.getText(sf)) : null,
  }));

  const ret = (node) => (node.type ? collapse(node.type.getText(sf)) : null);

  const push = (kind, name, node) => declarations.push({
    kind, name, line: lineOf(node),
    params: params(node), returnType: ret(node), hasComment: hasComment(node),
  });

  const visit = (node) => {
    if (ts.isFunctionDeclaration(node) && node.name) {
      push('function', node.name.text, node);
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
      declarations.push({
        kind: 'interface', name: node.name.text, line: lineOf(node),
        params: [], returnType: null, hasComment: hasComment(node),
      });
      for (const m of node.members) {
        const mn = m.name && m.name.getText ? m.name.getText(sf) : null;
        if (!mn) continue;
        if (ts.isMethodSignature(m)) push('method', mn, m);
        else if (ts.isPropertySignature(m)) {
          declarations.push({
            kind: 'property', name: mn, line: lineOf(m),
            params: [], returnType: null, hasComment: hasComment(m),
          });
        }
      }
    } else if (ts.isTypeAliasDeclaration(node)) {
      declarations.push({
        kind: 'type', name: node.name.text, line: lineOf(node),
        params: [], returnType: null, hasComment: hasComment(node),
      });
    } else if (ts.isClassDeclaration(node)) {
      for (const m of node.members) {
        const mn = m.name && m.name.getText ? m.name.getText(sf) : null;
        if (!mn) continue;
        if (ts.isMethodDeclaration(m)) push('method', mn, m);
        else if (ts.isGetAccessor(m) || ts.isSetAccessor(m)) push('method', mn, m);
      }
    }
  };

  ts.forEachChild(sf, visit);
  declarations.sort((a, b) => a.line - b.line);
  return { declarations, errors };
}

function collapse(text) {
  return text.replace(/\s+/g, ' ').trim();
}
