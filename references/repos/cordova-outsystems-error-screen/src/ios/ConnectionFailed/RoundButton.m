//
//  RoundButton.m
//  OutSystems
//
//  Created by engineering on 04/07/16.
//
//

#import "RoundButton.h"

@implementation RoundButton



- (void)drawRect:(CGRect)rect {

    [[self layer] setMasksToBounds:YES];
    [self.layer setCornerRadius:5.0f];
    [self.layer setBorderColor:[UIColor whiteColor].CGColor];
    [self.layer setBorderWidth:2];
    
    [super drawRect:rect];
}




@end
