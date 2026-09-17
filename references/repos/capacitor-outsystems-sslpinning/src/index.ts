import { registerPlugin } from '@capacitor/core';

import type { OutSystemsSSLPinningPlugin } from './definitions';

const OutSystemsSSLPinning = registerPlugin<OutSystemsSSLPinningPlugin>('OutSystemsSSLPinning', {
  web: () => import('./web').then((m) => new m.OutSystemsSSLPinningWeb()),
});

export * from './definitions';
export { OutSystemsSSLPinning };
