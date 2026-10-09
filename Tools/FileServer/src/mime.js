const path = require('path');
const mime = require('mime');

const compressedExtensions = new Map([
    ['.br', 'br'],
    ['.gz', 'gzip']
]);

const charsetMimePattern = /^(text\/|application\/(javascript|json|xml)$)/;

const stripCompressionExtension = (pathName) => {
    const ext = path.extname(pathName).toLowerCase();
    if (!compressedExtensions.has(ext)) return pathName;
    return pathName.slice(0, -ext.length);
}

const lookup = (pathName) => {
    const uncompressedPath = stripCompressionExtension(pathName);
    const fileName = path.basename(uncompressedPath).toLowerCase();

    if (fileName.endsWith('.wasm') || fileName.includes('.wasm.')) {
        return 'application/wasm';
    }

    if (fileName.endsWith('.js') || fileName.includes('.js.')) {
        return 'application/javascript';
    }

    if (fileName.endsWith('.data') || fileName.includes('.data.')) {
        return 'application/octet-stream';
    }

    let ext = path.extname(uncompressedPath);
    ext = ext.split('.').pop();
    return mime.getType(ext) || mime.getType('txt');
}

const encoding = (pathName) => {
    const ext = path.extname(pathName).toLowerCase();
    return compressedExtensions.get(ext);
}

const shouldAppendCharset = (contentType) => {
    return charsetMimePattern.test(contentType);
}

module.exports = {
    lookup,
    encoding,
    shouldAppendCharset
};
