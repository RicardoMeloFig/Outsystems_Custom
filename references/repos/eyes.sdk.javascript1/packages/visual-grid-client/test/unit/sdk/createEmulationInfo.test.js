'use strict'
const {describe, it} = require('mocha')
const {expect} = require('chai')
const createEmulationInfo = require('../../../src/sdk/createEmulationInfo')

describe('createEmulationInfo', () => {
  it("return undefined if there's no emulationInfo", () => {
    const emulationInfo = createEmulationInfo({
      width: 1,
      height: 2,
    })

    expect(emulationInfo).to.equal(undefined)
  })

  it('handles deviceName', () => {
    const emulationInfo = createEmulationInfo({deviceName: 'bla'})
    expect(emulationInfo).to.eql({
      deviceName: 'bla',
      screenOrientation: undefined,
      device: undefined,
    })
  })

  it('handles device with deviceScaleFactor', () => {
    const emulationInfo = createEmulationInfo({
      width: 1,
      height: 2,
      deviceScaleFactor: 3,
    })
    expect(emulationInfo).to.eql({
      deviceName: undefined,
      screenOrientation: undefined,
      device: {
        width: 1,
        height: 2,
        deviceScaleFactor: 3,
        mobile: undefined,
      },
    })
  })

  it('handles device with mobile', () => {
    const emulationInfo = createEmulationInfo({
      width: 1,
      height: 2,
      mobile: true,
    })
    expect(emulationInfo).to.eql({
      deviceName: undefined,
      screenOrientation: undefined,
      device: {
        width: 1,
        height: 2,
        deviceScaleFactor: undefined,
        mobile: true,
      },
    })
  })
})
