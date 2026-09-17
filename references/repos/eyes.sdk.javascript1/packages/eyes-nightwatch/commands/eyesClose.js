const Events = require('events')

module.exports = class EyesClose extends Events {
  async command(args) {
    await this.client.api.globals.__eyes.close(args)
    this.emit('complete')
  }
}
