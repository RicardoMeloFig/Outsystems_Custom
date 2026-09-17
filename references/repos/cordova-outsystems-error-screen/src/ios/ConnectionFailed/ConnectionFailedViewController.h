//
//  ConnectionFailedViewController.h
//  OutSystems
//
//  Created by engineering on 22/07/16.
//
//

#import <UIKit/UIKit.h>

@protocol ConnectionFailedScreenDelegate <NSObject>

@optional

-(void) onConnectionFailedTryAgainItemTouched;

@end

@interface ConnectionFailedViewController : UIViewController

@property(nonatomic, weak) id<ConnectionFailedScreenDelegate> delegate;

@end
