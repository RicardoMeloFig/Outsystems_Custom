export interface CheckCertificateOptions {
  /**
   * The URL to check certificate on
   *
   * @since 1.0.0
   */
  url: string;
}

export interface OutSystemsSSLPinningPlugin {
  /**
   * Check permissions for the various Capacitor device APIs.
   *
   * @since 1.0.0
   */
  checkCertificate(options: CheckCertificateOptions): Promise<void>;
}
