//
//  ConnectionFailedViewController.m
//  OutSystems
//
//  Created by engineering on 22/07/16.
//
//

#import "ConnectionFailedViewController.h"
#import "RoundButton.h"

@interface ConnectionFailedViewController ()
@property (weak, nonatomic) IBOutlet UILabel *screenMessageLabel;
@property (weak, nonatomic) IBOutlet RoundButton *screenTryAgainButton;

@end

@implementation ConnectionFailedViewController


-(instancetype)init {
    if(self = [super initWithNibName:@"ConnectionFailed" bundle:nil]) {}
    return self;
}

-(void)viewDidLoad{
    [super viewDidLoad];
}

- (IBAction)onTryAgainTouched:(id)sender {
    [self removeFromParentViewController];
    [self.view removeFromSuperview];

    [_delegate onConnectionFailedTryAgainItemTouched];
}

@end
