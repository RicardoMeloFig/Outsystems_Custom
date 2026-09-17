//
//  LogOut.h
//  OutSystems
//
//  Created by Vitor Oliveira on 26/11/14.
//	@author Vítor Oliveira (vitor.oliveira@outsystems.com)
//	@author João Pereira Gonçalves (joao.goncalves@outsystems.com)
//

#import <Cordova/CDVPlugin.h>
#import "OutSystemsAppDelegate.h"

@interface LogOut : CDVPlugin

- (OutSystemsAppDelegate*)appDelegate;
- (void)logOut:(CDVInvokedUrlCommand*)command;
- (void)logOutWebView:(CDVInvokedUrlCommand*)command;

@end
