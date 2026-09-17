//
//  BeaconsManager.m
//  OutSystems
//
//  Created by Vitor Oliveira on 19/03/16.
//
//

#import "BeaconsManager.h"

@implementation BeaconsManager

@synthesize notifications;

#pragma mark Singleton Methods

+ (id)sharedManager {
    static BeaconsManager *sharedMyManager = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        sharedMyManager = [[self alloc] init];
    });
    return sharedMyManager;
}

- (id)init {
    if (self = [super init]) {
        notifications = [NSMutableArray array];
    }
    return self;
}

- (void) addNewNotification: (UILocalNotification *) notification {
    [notifications addObject:notification];
}

-(NSMutableArray *) getNotifications {
    return notifications;
}

- (void) removeNotification: (UILocalNotification *) notification {
    [notifications removeObject:notification];
}

- (BOOL) clearNotifications {
    [notifications removeAllObjects];
    return YES;
}

@end
