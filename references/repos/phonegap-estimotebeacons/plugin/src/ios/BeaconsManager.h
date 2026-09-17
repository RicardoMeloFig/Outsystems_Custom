//
//  BeaconsManager.h
//  OutSystems
//
//  Created by Vitor Oliveira on 19/03/16.
//
//

#import <Foundation/Foundation.h>

@interface BeaconsManager : NSObject {
    NSMutableArray *notifications;
}

@property (nonatomic, retain) NSMutableArray *notifications;

+ (id)sharedManager;

- (void) addNewNotification: (UILocalNotification *) notification;
- (NSMutableArray *) getNotifications;
- (void) removeNotification: (UILocalNotification *) notification;
- (BOOL) clearNotifications;


@end
