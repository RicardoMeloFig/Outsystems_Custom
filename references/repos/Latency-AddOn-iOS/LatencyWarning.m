//
//  LatencyWarning.m
//  OutSystems
//
//  Created by Vitor Oliveira on 21/04/15.
//
//

#import "LatencyWarning.h"
#import "LatencyDetection.h"

@implementation LatencyWarning

    static double TIME_SCHEDULE = 180; //Time defined in seconds
    static double LATENCY_LIMIT = 600; //Time defined in milliseconds
    static double DELAY_IN_SECONDS = 15.0;
    static int COUNTER_RETRIES = 8;

- (id)init
{
    self = [super init];
    if (self) {
        [self setup];
    }
    return self;
}

- (id) initWithHostName: (NSString *) hostName andMainView: (UIView *) view
{
    _hostName = hostName;
    self.view = view;
    self = [super init];
  //  if (self) {
  //      [self setup];
  //  }
    return self;
}
- (id)initWithFrame:(CGRect)frame
{
    self = [super initWithFrame:frame];
    if (self) {
        [self setup];
    }
    return self;
}

- (id)initWithCoder:(NSCoder *)aDecoder
{
    self = [super initWithCoder:aDecoder];
    if (self) {
        [self setup];
    }
    return self;
}

- (void)setup
{
    self.counterTry = 7;
    [self checkLatency];
    self.timer = [NSTimer scheduledTimerWithTimeInterval:TIME_SCHEDULE target:(self) selector:@selector(checkLatency) userInfo:nil repeats:YES];
}

-(void) checkLatency
{
    [LatencyDetection pingHostname:_hostName andResultCallback:^(NSString *latency) {
                     
                     NSLog(@"Your latency is: %@", latency ? latency : @"unknown");
                     
                     if(latency) {
                         double latencyResult = [latency doubleValue];
                         if(latencyResult > LATENCY_LIMIT) {
                             if(!_latencyError) {
                                 if(self.counterTry == COUNTER_RETRIES) {
                                     
                                     _latencyError = [[UIView alloc] initWithFrame:CGRectMake(0, 0, self.view.frame.size.width, 40)];
                                     //_latencyError.backgroundColor = [UIColor redColor];
                                     _latencyError.backgroundColor = [UIColor colorWithRed:74.0/255.0f green:73.0/255.0f blue:74.0/255.0f alpha:0.95];
                                     UILabel *errorLabel = [[UILabel alloc] initWithFrame:CGRectMake(0, 0, self.view.frame.size.width, 40)];
                                     errorLabel.text = @"Slow connection detected";
                                     errorLabel.textColor = [UIColor whiteColor];
                                     errorLabel.textAlignment = NSTextAlignmentCenter;
                                     errorLabel.font = [UIFont fontWithName:@"OpenSans" size:14];
                                     
                                     UIButton *buttonInfo = [[UIButton alloc] initWithFrame:CGRectMake(self.view.frame.size.width-40, 10, 20, 20)];
                                     [buttonInfo setBackgroundImage:[UIImage imageNamed: @"icon-white-help.png"] forState:UIControlStateNormal];
                                     
                                     [buttonInfo addTarget:self action:@selector(btnInfoLatencyClicked) forControlEvents:UIControlEventTouchUpInside];
                                     buttonInfo.translatesAutoresizingMaskIntoConstraints = YES;
                                     
                                     [_latencyError addSubview:errorLabel];
                                     [_latencyError addSubview:buttonInfo];
                                     errorLabel.autoresizingMask = UIViewAutoresizingFlexibleWidth;
                                     buttonInfo.autoresizingMask = UIViewAutoresizingFlexibleLeftMargin;
                                     
                                     // Animation to add latency view
                                     [self.view addSubview:self.latencyError];
                                     
                                     self.latencyError.translatesAutoresizingMaskIntoConstraints = YES;
                                     _latencyError.frame =  CGRectMake(0, -40, self.view.frame.size.width, 40);// somewhere offscreen, in the direction you want it to appear from
                                     [UIView animateWithDuration:0.5
                                                      animations:^{
                                                          _latencyError.frame = CGRectMake(0, 0, self.view.frame.size.width, 40);// its final location
                                                      }];
                                     
                                     
                                     _latencyError.autoresizingMask = UIViewAutoresizingFlexibleWidth;
                                     [errorLabel setCenter:CGPointMake(self.view.frame.size.width / 2, 20)];
                                     
                                     self.counterTry = 0;
                                     dispatch_time_t popTime = dispatch_time(DISPATCH_TIME_NOW, (int64_t)(DELAY_IN_SECONDS * NSEC_PER_SEC));
                                     dispatch_after(popTime, dispatch_get_main_queue(), ^(void){
                                         [self hideLatencyView];
                                     });
                                     
                                 } else {
                                     self.counterTry += 1;
                                 }
                                 
                             }
                         } else {
                             [self hideLatencyView];
                         }
                     }
                 }];
}

-(void) hideLatencyView {
    if (self.latencyError && !self.viewMessage) {
        
        if(self.viewMessage) {
            [_buttonClose setHidden:YES];
            [UIView animateWithDuration:0.5
                                  delay:0
                                options:UIViewAnimationOptionCurveEaseInOut
                             animations:^{
                                 _infoLabel.frame = CGRectMake(0, 10, self.view.frame.size.width, 0);
                                 _viewMessage.frame =  CGRectMake(0, 40, self.view.frame.size.width, 0);
                             } completion:^(BOOL finished) {
                                 [self.viewMessage removeFromSuperview];
                                 self.viewMessage = nil;
                             }];
        }
        
        // Animation to remove latency view
        [UIView animateWithDuration:0.5
                              delay:0.0
                            options:UIViewAnimationOptionCurveEaseInOut
                         animations:^{
                             _latencyError.frame =  CGRectMake(0, -30, self.view.frame.size.width, 30);
                         } completion:^(BOOL finished) {
                             [self.latencyError removeFromSuperview];
                         }];
        self.latencyError = nil;
    }
}

-(void) btnInfoLatencyClicked {
    if(!self.viewMessage) {
        // Add View with Information about latency
        self.viewMessage = [[UIView alloc] initWithFrame:CGRectMake(0, 40, self.view.frame.size.width, 150)];
        self.viewMessage.backgroundColor = [UIColor colorWithRed:53.0/255.0f green:53.0/255.0f blue:53.0/255.0f alpha:0.95];
        
        // Label with information about latency
        _infoLabel = [[UILabel alloc] initWithFrame:CGRectMake(0, 10, self.view.frame.size.width, 90)];
        _infoLabel.textColor = [UIColor whiteColor];
        _infoLabel.font = [UIFont fontWithName:@"OpenSans" size:14];
        _infoLabel.lineBreakMode = NSLineBreakByWordWrapping;
        _infoLabel.numberOfLines = 0;
        NSString *html =
        @"<html>"
        "  <head>"
        "    <style type='text/css'>"
        "      p {"
        "       margin-top: 0px;"
        "       margin-bottom: 0px;"
        "       margin-right: 10px;"
        "       margin-left: 10px;"
        "       font-family: 'Open Sans';"
        "       font-size: 11px;"
        "       }"
        "     ul {"
        "       font-family: 'Open Sans';"
        "       font-size: 11px;"
        "       }"
        "    </style>"
        "  </head>"
        "  <body leftmargin='50'><p><font color='white'>Your connection is getting low, we detect a High-Latency in your network, probably<br>"
        "<ul style='list-style-type:circle'>"
        "<li>Your device is too far from the access point.</li>"
        "<li>Are in a place with low signal.</li>"
        "<li>There is interference from other devices.</li>"
        "</ul></font></p>"
        "</body>"
        "</html>";
        
        NSError *err = nil;
        _infoLabel.attributedText = [[NSAttributedString alloc]
                                     initWithData: [html dataUsingEncoding:NSUTF8StringEncoding]
                                     options: @{ NSDocumentTypeDocumentAttribute: NSHTMLTextDocumentType }
                                     documentAttributes: nil
                                     error: &err];
        if(err)
            NSLog(@"Unable to parse label text: %@", err);
        
        
        _infoLabel.autoresizingMask = UIViewAutoresizingFlexibleWidth;

        // Button to close the view message
        _buttonClose = [[UIButton alloc] initWithFrame:CGRectMake(((self.view.frame.size.width/2) - 88.5), 110, 177, 30)];
        [_buttonClose setTitle: @"Ok, I undersand this." forState: UIControlStateNormal];
        _buttonClose.titleLabel.font = [UIFont systemFontOfSize:14];
        [[_buttonClose layer] setBorderWidth:0.5f];
        [[_buttonClose layer] setCornerRadius:5.0f];
        [[_buttonClose layer] setBorderColor:[UIColor whiteColor].CGColor];
        [_buttonClose addTarget:self action:@selector(btnCloseLatencyInfo) forControlEvents:UIControlEventTouchUpInside];
        _buttonClose.autoresizingMask = UIViewAutoresizingFlexibleRightMargin;
        
        // [self.viewMessage addSubview:_buttonClose];
        
        // Add Subview to the main view
        [self.view addSubview:self.viewMessage];
        _viewMessage.autoresizingMask = UIViewAutoresizingFlexibleWidth;
        self.viewMessage.frame =  CGRectMake(0, 40, self.view.frame.size.width, 0);// somewhere offscreen, in the direction you want it to appear from
        [UIView animateWithDuration:0.5
                         animations:^{
                             _viewMessage.frame = CGRectMake(0, 40, self.view.frame.size.width, 150);// its final location
                             
                         } completion:^(BOOL finished) {
                             [self.viewMessage addSubview:_infoLabel];
                             [self.viewMessage addSubview:_buttonClose];
                         }];
    }
}

-(void) btnCloseLatencyInfo {
    [_buttonClose setHidden:YES];
    
    // Animation to remove latency view
    [UIView animateWithDuration:0.5
                          delay:0
                        options:UIViewAnimationOptionCurveEaseInOut
                     animations:^{
                         _infoLabel.frame = CGRectMake(0, 10, self.view.frame.size.width, 0);
                         _viewMessage.frame =  CGRectMake(0, 40, self.view.frame.size.width, 0);
                     } completion:^(BOOL finished) {
                         [self.viewMessage removeFromSuperview];
                         self.viewMessage = nil;
                         [self hideLatencyView];
                     }];
}

@end
