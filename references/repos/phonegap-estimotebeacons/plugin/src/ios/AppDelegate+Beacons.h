//
//  OutSystemsAppDelegate+Beacons.h
//  OutSystems
//
//  Created by Danilo Costa on 08/03/16.
//
//

#import "AppDelegate.h"
#import <EstimoteSDK/ESTBeaconManager.h>
#import <objc/runtime.h>

@interface AppDelegate (Beacons) <ESTBeaconManagerDelegate>

@property (strong, nonatomic) ESTBeaconManager *beaconManager;
- (BOOL) beacons_application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions;
-(void) beacons_application:(UIApplication *)application didReceiveLocalNotification:(UILocalNotification *)notification;

@end
