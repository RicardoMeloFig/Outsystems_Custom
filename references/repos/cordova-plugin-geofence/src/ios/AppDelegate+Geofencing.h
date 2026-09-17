//
//  AppDelegate+Geofencing.h
//  OutSystems
//
//  Created by Vitor Oliveira on 09/03/16.
//
//

#import "AppDelegate.h"
#import <CoreLocation/CLLocationManager.h>
#import <objc/runtime.h>

@interface AppDelegate (Geofencing) <CLLocationManagerDelegate>

@property (strong, nonatomic) CLLocationManager * locationManager;
- (BOOL) geofencing_application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions;
-(void) geofence_application:(UIApplication *)application didReceiveLocalNotification:(UILocalNotification *)notification;

@end
