'use strict'
const FakeEyesWrapper = require('./FakeEyesWrapper')

function createFakeWrapper(baseUrl, options) {
  return new FakeEyesWrapper(
    Object.assign(
      {
        goodFilename: 'test.cdt.json',
        goodResourceUrls: [`${baseUrl}/smurfs.jpg`, `${baseUrl}/test.css`],
        goodTags: ['good1', 'good2'],
      },
      options,
    ),
  )
}

module.exports = createFakeWrapper
