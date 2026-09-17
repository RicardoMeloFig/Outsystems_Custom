import { WebPlugin } from '@capacitor/core';

import type { CheckCertificateOptions, OutSystemsSSLPinningPlugin } from './definitions';

export class OutSystemsSSLPinningWeb extends WebPlugin implements OutSystemsSSLPinningPlugin {
  async checkCertificate(options: CheckCertificateOptions): Promise<never> {
    throw this.unavailable(`This plugin is not available in the browser - cannot check certificate for ${options.url}`);
  }
}
