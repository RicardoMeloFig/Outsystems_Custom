import { WebPlugin } from '@capacitor/core';
export class OutSystemsSSLPinningWeb extends WebPlugin {
    async checkCertificate(options) {
        throw this.unavailable(`This plugin is not available in the browser - cannot check certificate for ${options.url}`);
    }
}
