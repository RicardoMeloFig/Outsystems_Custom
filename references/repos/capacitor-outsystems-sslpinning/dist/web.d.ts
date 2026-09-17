import { WebPlugin } from '@capacitor/core';
import type { CheckCertificateOptions, OutSystemsSSLPinningPlugin } from './definitions';
export declare class OutSystemsSSLPinningWeb extends WebPlugin implements OutSystemsSSLPinningPlugin {
    checkCertificate(options: CheckCertificateOptions): Promise<never>;
}
