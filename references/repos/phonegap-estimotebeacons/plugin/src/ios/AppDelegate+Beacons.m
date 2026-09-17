//
//  OutSystemsAppDelegate+Beacons.m
//  OutSystems
//
//  Created by Danilo Costa on 08/03/16.
//
//

#import "AppDelegate+Beacons.h"
#import "BeaconsManager.h"

ESTBeaconManager *knewbeaconManager;
@implementation AppDelegate (Beacons)



@dynamic beaconManager;

- (void)setBeaconManager:(ESTBeaconManager *)beaconManager
{
    objc_setAssociatedObject(self, (__bridge const void *)(knewbeaconManager), beaconManager, OBJC_ASSOCIATION_ASSIGN);
}

- (id)beaconManager
{
    return objc_getAssociatedObject(self, (__bridge const void *)(knewbeaconManager));
}

+ (void)load {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        
        Class class = [self class];
        
        SEL originalSelector = @selector(application:didFinishLaunchingWithOptions:);
        SEL swizzledSelector = @selector(beacons_application:didFinishLaunchingWithOptions:);
        
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
        SEL swizzledSelectorNoti = @selector(beacons_application:didReceiveLocalNotification:);
        
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

- (BOOL) beacons_application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
     NSLog(@"---------- beacons_application_didFinishLaunchingWithOptions ----------");
    knewbeaconManager = [ESTBeaconManager new];
    knewbeaconManager.delegate = self;
    [knewbeaconManager requestAlwaysAuthorization];
    
    // Request to Permissions to Send Local Notifications
    if ([UIApplication instancesRespondToSelector:@selector(registerUserNotificationSettings:)]) {
        [[UIApplication sharedApplication] registerUserNotificationSettings:[UIUserNotificationSettings settingsForTypes:UIUserNotificationTypeAlert|UIUserNotificationTypeSound categories:nil]];
    }
    
    return [self beacons_application:application didFinishLaunchingWithOptions:launchOptions];
}

-(void) beacons_application:(UIApplication *)application didReceiveLocalNotification:(UILocalNotification *)notification {
    NSLog(@"Did Receive Local Notification Delegate - Beacons");
    
    if(notification != nil && notification.userInfo && [notification.userInfo objectForKey:@"beacon.notification.data"]) {
        NSDictionary *userInfo = notification.userInfo;
        
        NSURL *siteURL = [NSURL URLWithString:[userInfo objectForKey:@"deeplink"]];
        
        if(siteURL && [userInfo objectForKey:@"deeplink"] && ![[userInfo objectForKey:@"deeplink"] isEqualToString:@""]) {
            dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
                [[UIApplication sharedApplication] openURL:siteURL];
            });
        }
        [[NSNotificationCenter defaultCenter] postNotificationName:@"CDVLocalNotificationBeacon" object:notification];
    }
    
    [self beacons_application:application didReceiveLocalNotification:notification];
}

-(void)beaconManager:(ESTBeaconManager *)manager didEnterRegion:(CLBeaconRegion *)region
{
    UIApplicationState state = [[UIApplication sharedApplication] applicationState];
    if (state == UIApplicationStateBackground || state == UIApplicationStateInactive)
    {
        
        NSArray *paths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
        NSString *documentsDirectory = [paths objectAtIndex:0];
        NSString *path = [documentsDirectory stringByAppendingPathComponent:@"Beacons.plist"];
        NSMutableDictionary *myDictionary=[[NSMutableDictionary alloc] initWithContentsOfFile:path];
        
        NSMutableDictionary *beacondata = [myDictionary objectForKey:region.identifier];
        NSString *enterMessage = [beacondata objectForKey:@"enterMessage"];
        if(enterMessage.length > 0)
        {
            UILocalNotification *notification = [[UILocalNotification alloc] init];
            
            
            NSDate *now =[NSDate date];
            //NSDate *lastNotification = [beacondata objectForKey:@"sentnotification"];
            NSDateFormatter *formatter = [[NSDateFormatter alloc] init];
            [formatter setDateFormat:@"yyyy-MM-dd'T'HH:mm:ss.SSSZ"];
            NSString *strLastNotification = [beacondata objectForKey:@"sentnotification"];
            NSDate *lastNotification = [formatter dateFromString:strLastNotification];
            
            NSInteger mins = 0;
            if(lastNotification != nil)
            {
                NSTimeInterval distanceBetweenDates = [now timeIntervalSinceDate:lastNotification];
                long seconds = lroundf(distanceBetweenDates);
                 mins = (seconds % 3600) / 60;
            }
            else{mins = 9999999;}
            
            int verify = [[beacondata objectForKey:@"idle"] integerValue];
           

            if(verify == 0)
                [beacondata setValue:0 forKey:@"idle"];
            
            if(mins >= verify || [beacondata objectForKey:@"idle"] == 0)
            {
                
                
                if (NSFoundationVersionNumber > NSFoundationVersionNumber_iOS_8_1) {
                    notification.alertTitle = [beacondata objectForKey:@"enterTitle"];
                }
                notification.alertBody =[beacondata objectForKey:@"enterMessage"];
                notification.soundName = UILocalNotificationDefaultSoundName;
                [beacondata setValue:@"inside" forKey:@"state"];
                [beacondata setValue:@"true" forKey:@"openedFromNotification"];
                //set up user info dicionary
                NSMutableDictionary *userInfoDict =[[NSMutableDictionary alloc] init];
                [userInfoDict setObject:beacondata forKey:@"beacon.notification.data"];
                [userInfoDict setValue:@"beacon-monitor-enter" forKey:@"event"];
                if([beacondata objectForKey:@"deeplink"])
                    [userInfoDict setValue:[beacondata objectForKey:@"deeplink"] forKey:@"deeplink"];
                notification.userInfo = userInfoDict;
                [[UIApplication sharedApplication] presentLocalNotificationNow:notification];
                //NSDate *nowregister = [NSDate date];
                
                NSDateFormatter *formatter = [[NSDateFormatter alloc] init];
                [formatter setDateFormat:@"yyyy-MM-dd'T'HH:mm:ss.SSSZ"];
                NSString *dateString = [formatter stringFromDate:[NSDate date]];
                
                [beacondata setValue:dateString forKey:@"sentnotification"];
                [myDictionary setObject:beacondata forKey:region.identifier];
                [myDictionary writeToFile:path atomically:YES];
            }
        }
    }
}

-(void)beaconManager:(ESTBeaconManager *)manager didExitRegion:(CLBeaconRegion *)region
{
    UIApplicationState state = [[UIApplication sharedApplication] applicationState];
    if (state == UIApplicationStateBackground || state == UIApplicationStateInactive)
    {
        NSArray *paths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
        NSString *documentsDirectory = [paths objectAtIndex:0];
        NSString *path = [documentsDirectory stringByAppendingPathComponent:@"Beacons.plist"];
        NSMutableDictionary *myDictionary=[[NSMutableDictionary alloc] initWithContentsOfFile:path];
        
        NSMutableDictionary *beacondata = [myDictionary objectForKey:region.identifier];
        NSString *exitMessage = [beacondata objectForKey:@"exitMessage"];
        if(exitMessage.length > 0)
        {
            
            NSDate *now =[NSDate date];
            
            //NSDate *lastNotification = [beacondata objectForKey:@"sentnotification"];
            NSDateFormatter *formatter = [[NSDateFormatter alloc] init];
            [formatter setDateFormat:@"yyyy-MM-dd'T'HH:mm:ss.SSSZ"];
            NSString *strLastNotification = [beacondata objectForKey:@"sentnotification"];
            NSDate *lastNotification = [formatter dateFromString:strLastNotification];
            NSInteger mins = 0;
            if(lastNotification != nil)
            {
                NSTimeInterval distanceBetweenDates = [now timeIntervalSinceDate:lastNotification];
                long seconds = lroundf(distanceBetweenDates);
                mins = (seconds % 3600) / 60;
            } else{
                mins = 9999999;
            }
            
            
            int verify = [[beacondata objectForKey:@"idle"] integerValue];
            
            if(verify == 0)
                [beacondata setValue:0 forKey:@"idle"];
            
            if(mins >= verify || [beacondata objectForKey:@"idle"] == 0)
            {
                UILocalNotification *notification = [[UILocalNotification alloc] init];
                if (NSFoundationVersionNumber > NSFoundationVersionNumber_iOS_8_1) {
                    notification.alertTitle = [beacondata objectForKey:@"exitTitle"];
                }
                notification.alertBody = [beacondata objectForKey:@"exitMessage"];
                notification.soundName = UILocalNotificationDefaultSoundName;
                [beacondata setValue:@"outside" forKey:@"state"];
                [beacondata setValue:@"true" forKey:@"openedFromNotification"];
                //set up user info dicionary
                NSMutableDictionary *userInfoDict =[[NSMutableDictionary alloc] init];
                [userInfoDict setObject:beacondata forKey:@"beacon.notification.data"];
                [userInfoDict setValue:@"beacon-monitor-exit" forKey:@"event"];
                
                if([beacondata objectForKey:@"deeplink"])
                    [userInfoDict setValue:[beacondata objectForKey:@"deeplink"] forKey:@"deeplink"];
                notification.userInfo = userInfoDict;
                [[UIApplication sharedApplication] presentLocalNotificationNow:notification];
                
               // NSDate *nowregister = [NSDate date];
                
                NSDateFormatter *formatter = [[NSDateFormatter alloc] init];
                [formatter setDateFormat:@"yyyy-MM-dd'T'HH:mm:ss.SSSZ"];
                NSString *dateString = [formatter stringFromDate:[NSDate date]];
                
                [beacondata setValue:dateString forKey:@"sentnotification"];
                
                //[beacondata setValue:nowregister forKey:@"sentnotification"];
                [myDictionary setObject:beacondata forKey:region.identifier];
                [myDictionary writeToFile:path atomically:YES];
            }
        }
    }
}
@end
