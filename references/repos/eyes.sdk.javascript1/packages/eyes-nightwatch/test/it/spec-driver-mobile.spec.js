const assert = require('assert')
const spec = require('../../src/spec-driver')
const fakeCaps = require('./fixtures/fake-caps-android')

// NOTE: to run this against a real mobile configuration
// - comment out fakeCaps
// - run with: npx nightwatch --env browserstack.android test/it/spec-driver-mobile.spec.js

// TODO: test against iOS
describe('spec driver', async () => {
  describe('mobile driver (@mobile)', async () => {
    before(function(_driver, done) {
      done()
    })
    after(function(driver, done) {
      return driver.end(function() {
        done()
      })
    })
    it('isMobile()', function(driver) {
      const {isMobile} = spec.getDriverInfo(driver, {using: fakeCaps})
      assert.ok(isMobile)
    })
    it('getDeviceName()', function(driver) {
      const {deviceName} = spec.getDriverInfo(driver, {using: fakeCaps})
      assert.deepStrictEqual(deviceName, 'google pixel 2')
    })
    it('getPlatformName()', function(driver) {
      const {platformName} = spec.getDriverInfo(driver, {using: fakeCaps})
      assert.deepStrictEqual(platformName, 'Android')
    })
    // TODO: test on Sauce
    it('getPlatformVersion()', function(driver) {
      const {platformVersion} = spec.getDriverInfo(driver, {using: fakeCaps})
      assert.deepStrictEqual(platformVersion, '9.0')
    })
    // TODO: test w/ orientation set on BS (fake captured w/o it)
    // TODO: test on Sauce
    it('getOrientation()', function(driver) {
      const result = spec.getOrientation(driver, {using: fakeCaps})
      assert.strictEqual(result, 'portrait')
    })
    it('isNative()', function(driver) {
      const {isNative} = spec.getDriverInfo(driver, {using: fakeCaps})
      assert.strictEqual(isNative, true)
    })
  })
})
