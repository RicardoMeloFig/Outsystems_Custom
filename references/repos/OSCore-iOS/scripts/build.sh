cd ..

rm -rf build

xcodebuild archive \
-scheme OSCore \
-configuration Release \
-destination 'generic/platform=iOS Simulator' \
-archivePath './scripts/build/OSCore.framework-iphonesimulator.xcarchive' \
SKIP_INSTALL=NO \
BUILD_LIBRARIES_FOR_DISTRIBUTION=YES


xcodebuild archive \
-scheme OSCore \
-configuration Release \
-destination 'generic/platform=iOS' \
-archivePath './scripts/build/OSCore.framework-iphoneos.xcarchive' \
SKIP_INSTALL=NO \
BUILD_LIBRARIES_FOR_DISTRIBUTION=YES


xcodebuild -create-xcframework \
-framework './scripts/build/OSCore.framework-iphonesimulator.xcarchive/Products/Library/Frameworks/OSCore.framework' \
-framework './scripts/build/OSCore.framework-iphoneos.xcarchive/Products/Library/Frameworks/OSCore.framework' \
-output './scripts/build/OSCore.xcframework'