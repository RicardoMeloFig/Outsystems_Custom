//
//  LatencyWarning.h
//  OutSystems
//
//  Created by Vitor Oliveira on 21/04/15.
//
//

#import <UIKit/UIKit.h>

@interface LatencyWarning : UIView

@property (strong, nonatomic) NSTimer *timer;
@property (strong, nonatomic) UIView *latencyError;
@property (nonatomic, assign) int counterTry;
@property (strong, nonatomic) NSString *hostName;
@property (strong, nonatomic) UIView *view;

@property (strong, nonatomic) UIView *viewMessage;
@property (strong, nonatomic) UIButton *buttonClose;
@property (strong, nonatomic) UILabel *infoLabel;

- (id) initWithHostName: (NSString *) hostName andMainView: (UIView *) view;


@end
