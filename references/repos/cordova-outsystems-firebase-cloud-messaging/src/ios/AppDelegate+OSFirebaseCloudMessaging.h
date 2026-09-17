// The old plugin made the assumption that AppDelegate existed
// and was written in Objective-C. Since the Capacitor template
// defaults to `AppDelegate.swift` we piggy back on Cordova's

#if __has_include("AppDelegate.h")
    #import "AppDelegate.h"
#else
    #if __has_include(<Cordova/AppDelegate.h>)
        #import <Cordova/AppDelegate.h>
    #endif
#endif

@interface AppDelegate (OSFirebaseCloudMessaging) <UIApplicationDelegate>
@end
