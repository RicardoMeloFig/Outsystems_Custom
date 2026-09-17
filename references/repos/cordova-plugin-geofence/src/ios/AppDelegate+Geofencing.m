//
//  AppDelegate+Geofencing.m
//  OutSystems
//
//  Created by Vitor Oliveira on 09/03/16.
//
//

#import "AppDelegate+Geofencing.h"
#import <CoreLocation/CLCircularRegion.h>
#import "OutSystems-Swift.h"
#import "GeofenceHelper.h"

CLLocationManager *knewLocationManager;
@implementation AppDelegate (Geofencing)

@dynamic locationManager;

- (void)setLocationManager:(CLLocationManager *)locationManager
{
    objc_setAssociatedObject(self, (__bridge const void *)(knewLocationManager), locationManager, OBJC_ASSOCIATION_ASSIGN);
}

- (id)locationManager
{
    return objc_getAssociatedObject(self, (__bridge const void *)(knewLocationManager));
}

+ (void)load {
    
    
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        
        Class class = [self class];
        
        SEL originalSelector = @selector(application:didFinishLaunchingWithOptions:);
        SEL swizzledSelector = @selector(geofencing_application:didFinishLaunchingWithOptions:);
        
        Method originalMethod = class_getInstanceMethod(class, originalSelector);
        Method swizzledMethod = class_getInstanceMethod(class, swizzledSelector);
        
        BOOL didAddMethod = class_addMethod(class, originalSelector, method_getImplementation(swizzledMethod), method_getTypeEncoding(swizzledMethod));
        
        if (didAddMethod) {
            class_replaceMethod(class, swizzledSelector, method_getImplementation(originalMethod), method_getTypeEncoding(originalMethod));
        } else {
            method_exchangeImplementations(originalMethod, swizzledMethod);
        }
        
        //
        SEL originalSelectorNoti = @selector(application:didReceiveLocalNotification:);
        SEL swizzledSelectorNoti = @selector(geofence_application:didReceiveLocalNotification:);
        
        Method originalMethodNoti = class_getInstanceMethod(class, originalSelectorNoti);
        Method swizzledMethodNoti = class_getInstanceMethod(class, swizzledSelectorNoti);
        
        BOOL didAddMethodNoti = class_addMethod(class, originalSelectorNoti, method_getImplementation(swizzledMethodNoti), method_getTypeEncoding(swizzledMethodNoti));
        
        if (didAddMethodNoti) {
            class_replaceMethod(class, swizzledSelectorNoti, method_getImplementation(originalMethodNoti), method_getTypeEncoding(originalMethodNoti));
        } else {
            method_exchangeImplementations(originalMethodNoti, swizzledMethodNoti);
        }
        
    });
}

- (BOOL) geofencing_application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
    
    knewLocationManager = [CLLocationManager new];
    knewLocationManager.delegate = self;
    [knewLocationManager requestAlwaysAuthorization];
    
    if ([UIApplication instancesRespondToSelector:@selector(registerUserNotificationSettings:)]) {
        [[UIApplication sharedApplication] registerUserNotificationSettings:[UIUserNotificationSettings settingsForTypes:UIUserNotificationTypeAlert|UIUserNotificationTypeSound categories:nil]];
    }
    
    return [self geofencing_application:application didFinishLaunchingWithOptions:launchOptions];
}

-(void) locationManager:(CLLocationManager *)manager didEnterRegion:(CLRegion *)region
{
    UIApplicationState state = [[UIApplication sharedApplication] applicationState];
    if (state == UIApplicationStateInactive ||state == UIApplicationStateBackground)
    {
        if ([region isKindOfClass:[CLCircularRegion class]]){
            //get data from databse swift
            WrapperStore *wrapper = [[WrapperStore alloc] init];
            NSString * geofencing = [wrapper getGeofencingById:region.identifier];
            //get json to dicionary
            NSError *jsonError;
            NSData *objectData = [geofencing dataUsingEncoding:NSUTF8StringEncoding];
            NSMutableDictionary *parsedData = [NSJSONSerialization JSONObjectWithData:objectData
                                                                              options:NSJSONReadingMutableContainers
                                                                                error:&jsonError];
            
            //Check Notifications
            Boolean showNotification = [GeofenceHelper validateTimeIntervalWithDictionary:parsedData];
            //Compare with dates of event to validate if we should create the Local Notification
            
            if(showNotification) {
                UILocalNotification *notification = [[UILocalNotification alloc] init];
                // if (IsAtLeastiOSVersion(@"8.2"))
            
                //form dicionary from parced data
                if (NSFoundationVersionNumber > NSFoundationVersionNumber_iOS_8_1) {
                    notification.alertTitle = [[parsedData valueForKey:@"notification"] valueForKey:@"title"];
                }
                notification.alertBody = [[parsedData valueForKey:@"notification"] valueForKey:@"text"];
                notification.soundName = UILocalNotificationDefaultSoundName;
                //form dicionary to userInfo
                [parsedData setValue:@"true" forKey:@"openedFromNotification"];
                NSMutableDictionary *userInfoDici = [[NSMutableDictionary alloc]init];
                [userInfoDici setValue:@"inside" forKey:@"state"];
                
                NSError * err;
                NSData * jsonData = [NSJSONSerialization  dataWithJSONObject:parsedData options:0 error:&err];
                NSString * myString = [[NSString alloc] initWithData:jsonData   encoding:NSUTF8StringEncoding];
                
                [userInfoDici setObject:myString forKey:@"geofence.notification.data"];
                if([[parsedData valueForKey:@"notification"] valueForKey:@"deeplink"])
                    [userInfoDici setObject:[[parsedData valueForKey:@"notification"] valueForKey:@"deeplink"] forKey:@"deeplink"];
                notification.userInfo = userInfoDici;
                [[UIApplication sharedApplication] presentLocalNotificationNow:notification];
            }
        }
    }
}

-(void) locationManager:(CLLocationManager *)manager didExitRegion:(CLRegion *)region {
    UIApplicationState state = [[UIApplication sharedApplication] applicationState];
    if (state == UIApplicationStateInactive ||state == UIApplicationStateBackground)
    {
        if ([region isKindOfClass:[CLCircularRegion class]]){
            UILocalNotification *notification = [[UILocalNotification alloc] init];
            //  if (IsAtLeastiOSVersion(@"8.2"))
            
            //get data from databse swift
            WrapperStore *wrapper = [[WrapperStore alloc] init];
            NSString * geofencing = [wrapper getGeofencingById:region.identifier];
            //get json to dicionary
            NSError *jsonError;
            NSData *objectData = [geofencing dataUsingEncoding:NSUTF8StringEncoding];
            NSMutableDictionary *parsedData = [NSJSONSerialization JSONObjectWithData:objectData
                                                                              options:NSJSONReadingMutableContainers
                                                                                error:&jsonError];
            
            //Check Notifications
            Boolean showNotification = [GeofenceHelper validateTimeIntervalWithDictionary:parsedData];
            //Compare with dates of event to validate if we should create the Local Notification
            
            if(showNotification) {
                //form dicionary from parced data
                if (NSFoundationVersionNumber > NSFoundationVersionNumber_iOS_8_1) {
                    notification.alertTitle = [[parsedData valueForKey:@"notification"] valueForKey:@"title"];
                }
                notification.alertBody = [[parsedData valueForKey:@"notification"] valueForKey:@"text"];
                notification.soundName = UILocalNotificationDefaultSoundName;
                //form dicionary to userInfo
                [parsedData setValue:@"true" forKey:@"openedFromNotification"];
                NSMutableDictionary *userInfoDici = [[NSMutableDictionary alloc]init];
                [userInfoDici setValue:@"outside" forKey:@"state"];
               
                NSError * err;
                NSData * jsonData = [NSJSONSerialization  dataWithJSONObject:parsedData options:0 error:&err];
                NSString * myString = [[NSString alloc] initWithData:jsonData   encoding:NSUTF8StringEncoding];
                
                [userInfoDici setObject:myString forKey:@"geofence.notification.data"];
                if([[parsedData valueForKey:@"notification"] valueForKey:@"deeplink"])
                    [userInfoDici setObject:[[parsedData valueForKey:@"notification"] valueForKey:@"deeplink"] forKey:@"deeplink"];
                notification.userInfo = userInfoDici;
                
                [[UIApplication sharedApplication] presentLocalNotificationNow:notification];
            }
        }
    }
}
@end