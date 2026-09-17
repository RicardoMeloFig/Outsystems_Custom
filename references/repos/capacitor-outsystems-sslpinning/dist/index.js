import { registerPlugin } from '@capacitor/core';
const OutSystemsSSLPinning = registerPlugin('OutSystemsSSLPinning', {
    web: () => import('./web').then((m) => new m.OutSystemsSSLPinningWeb()),
});
export * from './definitions';
export { OutSystemsSSLPinning };
