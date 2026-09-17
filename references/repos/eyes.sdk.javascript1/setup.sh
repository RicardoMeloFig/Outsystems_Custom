export PUPPETEER_SKIP_CHROMIUM_DOWNLOAD=true
cd packages/sdk-shared
yarn install
yarn link
cd ../eyes-sdk-core
yarn install
yarn link
yarn link @applitools/sdk-shared
cd ../visual-grid-client
yarn install
yarn link
yarn link @applitools/sdk-shared
yarn link @applitools/eyes-sdk-core
cd ../eyes-selenium
yarn install
yarn link @applitools/sdk-shared
yarn link @applitools/eyes-sdk-core
yarn link @applitools/visual-grid-client
