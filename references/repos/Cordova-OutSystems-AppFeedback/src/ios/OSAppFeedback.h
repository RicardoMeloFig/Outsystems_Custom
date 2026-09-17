//
//  OSAppFeedback.h
//  OutSystems
//
//
//

#import <Cordova/CDVPlugin.h>
#import "AppDelegate.h"
#import <OutSystemsMobileECT/OutSystemsMobileECT.h>

@interface OSAppFeedback : CDVPlugin

@property (strong, nonatomic) OSMobileECTController *mobileECTController;

- (AppDelegate*)appDelegate;

- (void)deviceready:(CDVInvokedUrlCommand*)command;

- (void)isAvailable:(CDVInvokedUrlCommand*)command;

- (void)openECT:(CDVInvokedUrlCommand*)command;

@end