const { createHash } = require('node:crypto');
const { readFileSync, writeFileSync } = require('node:fs');
const path = require('node:path');
const { brotliCompressSync, constants, gzipSync } = require('node:zlib');

const webRoot = path.resolve(process.argv[2] || 'publish', 'wwwroot');
const indexPath = path.join(webRoot, 'index.html');
let html = readFileSync(indexPath, 'utf8');

for (const stylesheet of ['app.css', 'DsaWuerfelApp.styles.css']) {
    const css = readFileSync(path.join(webRoot, stylesheet));
    const version = createHash('sha256').update(css).digest('hex').slice(0, 16);
    const escapedName = stylesheet.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const linkPattern = new RegExp(
        `(<link\\b[^>]*?\\s+href\\s*=\\s*)(["'])${escapedName}(?:\\?[^"']*)?\\2`,
        'gi');
    let matches = 0;

    html = html.replace(linkPattern, (_, prefix, quote) => {
        matches++;
        return `${prefix}${quote}${stylesheet}?v=${version}${quote}`;
    });

    if (matches !== 1) {
        throw new Error(`Expected exactly one stylesheet link for ${stylesheet}, found ${matches}.`);
    }
}

// UTF-8 decoding and encoding retain an existing BOM and the original line endings.
const index = Buffer.from(html, 'utf8');
const gzip = gzipSync(index, { level: 9 });
const brotli = brotliCompressSync(index, {
    params: {
        [constants.BROTLI_PARAM_MODE]: constants.BROTLI_MODE_TEXT,
        [constants.BROTLI_PARAM_QUALITY]: 11
    }
});

writeFileSync(indexPath, index);
writeFileSync(`${indexPath}.gz`, gzip);
writeFileSync(`${indexPath}.br`, brotli);
