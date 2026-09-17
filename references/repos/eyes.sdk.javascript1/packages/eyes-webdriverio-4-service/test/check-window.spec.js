/* eslint-disable no-undef */
'use strict'

const {By, Target} = require('@applitools/eyes.webdriverio')

describe('CheckWindowTest', () => {
  beforeEach(() => {
    browser.url('http://applitools.github.io/demo/TestPages/FramesTestPage/')
  })

  it('checkWindow', () => {
    browser.eyesCheck('main')
  })

  it('checkWindow - no title', () => {
    browser.eyesCheck()
  })

  it.skip('checkRegion', () => {
    browser.eyesCheckWindow('main', Target.region(By.id('overflowing-div')))
  })

  it('checkFrame', () => {
    browser.eyesCheckWindow('main', Target.frame('frame1'))
  })
})
