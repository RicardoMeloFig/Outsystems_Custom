/* eslint-disable no-undef */
'use strict'

const {deepStrictEqual} = require('assert')

describe('ConfigurationTest', () => {
  beforeEach(() => {
    browser.url('http://applitools.github.io/demo/TestPages/FramesTestPage/')
  })

  it('checkWindow', () => {
    const actualViewportSize = browser.eyesGetConfiguration().getViewportSize()
    deepStrictEqual({width: 600, height: 500}, actualViewportSize.toJSON())
  })
})
