const Events = require('events')

module.exports = class EyesCheck extends Events {
  async command(args) {
    await this.client.api.globals.__eyes.check(args)
    this.emit('complete')
  }
}
