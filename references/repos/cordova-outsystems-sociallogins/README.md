# cordova-outsystems-sociallogins

This plugin is SUPPORTED by OutSystems. Customers entitled to Support Services may obtain assistance through Support.
  
## Installation

    cordova plugin add <path-to-repo-local-clone>

It is also possible to install via repo url directly

    cordova plugin add https://github.com/OutSystems/cordova-outsystems-sociallogins.git

  
## Supported Platforms

- Android
- iOS

## Methods

* [OSSocialLogins.loginApple](#module_sociallogins.loginApple)
* [OSSocialLogins.loginGoogle](#module_sociallogins.loginGoogle)
* [OSSocialLogins.loginFacebook](#module_sociallogins.loginFacebook)
* [OSSocialLogins.loginLinkedIn](#module_sociallogins.loginLinkedIn)

### Login Data returned by every method (JSON)


    {
      "id": "EXAMPLE_ID",
      "email": "EXAMPLE_EMAIL",
      "firstName": "EXAMPLE_FIRST_NAME",
      "lastName": "EXAMPLE_LAST_NAME",
      "token": "EXAMPLE_ACCESS_TOKEN",
      "picture": "EXAMPLE_PROFILE_PICTURE"
    }

<a name="module_sociallogins.loginApple"></a>
## OSSocialLogins.loginApple

Starts the login process using Apple as the login provider. Returns the login date corresponding to the Apple account used to sign in through the `loginSuccess` callback. If there is an error, the `loginError` callback is called with an `error` object containing both `error.code` and `error.message`.

    cordova.plugins.OSSocialLogins.loginApple(loginSuccess, loginError, state, clientId, redirectUrl);
### Parameters

- __loginSuccess__: The callback that is passed the login data after the login with Apple is successful.

- __loginError__: The callback that executes if an error occurs.

- __state__: A UUID that identifies the login request.

- __clientId__: The Identifier of the Sign-In with Apple Configuration on Apple Developer.

- __redirectUrl__: The redirect URL of the Sign-In with Apple Configuration on Apple Developer.

---

<a name="module_sociallogins.loginGoogle"></a>
## OSSocialLogins.loginGoogle

Starts the login process using Google as the login provider. Returns the login data corresponding to the Google account used to sign in through the `loginSuccess` callback. If there is an error, the `loginError` callback is called with an `error` object containing both `error.code` and `error.message`.

    cordova.plugins.OSSocialLogins.loginGoogle(loginSuccess, loginError, clientId, redirectUrl);
### Parameters

- __loginSuccess__: The callback that is passed the login data after the login with Google is successful.

- __loginError__: The callback that executes if an error occurs.

- __clientId__: The clientId of the Sign-In with Google configuration on the Google Cloud Platform.

- __redirectUrl__: The redirect URL of the Sign-In with Google configuration on the Google Cloud Platform.

---

<a name="module_sociallogins.loginFacebook"></a>
## OSSocialLogins.loginFacebook

Starts the login process using Facebook as the login provider. Returns the login data corresponding to the Apple account used to sign in through the `loginSuccess` callback. If there is an error, the `loginError` callback is called with an `error` object containing both `error.code` and `error.message`.

    cordova.plugins.OSSocialLogins.loginFacebook(loginSuccess, loginError);
### Parameters

- __loginSuccess__: The callback that is passed the login data after the login with Facebook is successful.

- __loginError__: The callback that executes if an error occurs.

---

<a name="module_sociallogins.loginLinkedIn"></a>
## OSSocialLogins.loginLinkedIn

Starts the login process using LinkedIn as the login provider. Returns the login data corresponding to the LinkedIn account used to sign in through the `loginSuccess` callback. If there is an error, the `loginError` callback is called with an `error` object containing both `error.code` and `error.message`.

    cordova.plugins.OSSocialLogins.loginLinkedIn(loginSuccess, loginError, state, clientId, redirectUrl);
### Parameters

- __loginSuccess__: The callback that is passed the login data after the login with LinkedIn is successful.

- __loginError__: The callback that executes if an error occurs.

- __state__: A UUID that identifies the login request.

- __clientId__: The clientId of the Sign-In with LinkedIn configuration on LinkedIn for Developers.

- __redirectUrl__: The redirect URL of the Sign-In with LinkedIn configuration on LinkedIn for Developers.

---

## Errors (for both Android and iOS)

| Code | Message                                                | Reason                                                                                                                                                                  |
|------|--------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 100  | There was an error signing in with Apple               | An error that we couldn't identify occurred while signing in with Apple.                                                                                                            |
| 101  | The login was cancelled                                | User cancelled the login. |
| 103  | Invalid token                                          | Token used for Apple token validation is invalid.                                                                                                     |
| 104  | There are input parameters missing                     | User-caused error due to missing parameters.                                                                                                                     |
| 105  | User id missing                                        | User ID for Apple is missing in the login response.                                                                                                           |
| 106  | Access token missing                                   | Access token for Apple is missing in the login response.                                |
| 200  | There was an error signing in with Google              | An error that we couldn't identify occurred while signing in with Google.                                              |
| 201  | Missing Google Sign In configuration                   | Google sign-in configuration for iOS not found.                                                                                                              |
| 201  | Access token missing                                   | Access token for Google in missing in the login response.                                                                                                              |
| 202  | User id missing                                        | User ID for Google is missing in the login response.                                                                                                              |
| 201  | Google configuration not valid                         | Google sign-in for iOS configuration is not valid.                                                                                                              |
| 300  | There was an error signing in with Facebook            | An error that we couldn't identify occurred while signing in with Facebook.                                                                                                               |
| 302  | Some error occurred while signing in with Facebook as no token was found.        | Access token for Facebook is missing in the login response.                                                                                                              |
| 303  | No results were returned while signing in with Facebook| No results found in the login response.                                                                                                               |
| 304  | Couldn't fetch information for all parameters requested            | Access to some Facebook account data is not allowed.                                                                                                               |
| 305  | There was a problem while request the user's data on Facebook      | An error that we couldn't identify occurred while requesting user data.                                                                                                               |
| 399  | The configurations for Facebook were not properly set            | Facebook login configuration is not valid.                                                                                                               |
| 400  | There was an error signing in with LinkedIn            | An error that we couldn't identify occurred while signing in with LinkedIn.                                                                                                               |
| 401  | There was an issue while creating the sign in request for LinkedIn            | Error in creating the sign in request for LinkedIn in iOS.                                                                                                               |
| 402  | There was an issue while creating the web authentication session for LinkedIn   | Error in creating the web authentication session for LinkedIn in iOS.                                                                                                               |
| 403  | Couldn't fetch information for all parameters requested            | Access to some LinkedIn account data is not allowed in iOS.                                                                                                               |
| 404  | Access token missing            | Access token for LinkedIn is missing in the login response.                                                                                                                |
| 405  | User id missing            | User ID for LinkedIn is missing in the login response.                                                                                                               |
| 499  | The configuration for LinkedIn were not properly set            | LinkedIn login configuration is not valid is iOS.                                                                                                               |
