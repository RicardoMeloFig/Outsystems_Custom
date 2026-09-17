'use strict'
const crypto = require('crypto')

function getSha256Hash(content) {
  return crypto
    .createHash('sha256')
    .update(content)
    .digest('hex')
}

module.exports = getSha256Hash
