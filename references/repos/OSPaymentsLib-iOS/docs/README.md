# OSPaymentsPluginLib-iOS

The `OSPaymentsPluginLib-iOS` is a library build using `Swift` that lets you set a payment experience using Apple Pay. It allows to set the payment's details, such as the merchant’s information, payment amount and currency, as well as shipping and billing address. This information can then used to process a payment within an app.

The `OSPMTActionDelegate` protocol, along with the class that implements it - `OSPMTPayments` - allows this interaction, providing the following operations:
- Setup Payment Configuration
- Check if Device is Ready for Payment
- Set Details and Trigger Payment

Each is detailed on following sections.

## Index

- [Motivation](#motivation)
- [Usage](#usage)
- [Methods](#methods)
    - [Setup Payment Configuration](#setup-payment-configuration)
    - [Check if Device is Ready for Payment](#check-if-device-is-ready-for-payment)
    - [Set Details and Trigger Payment](#set-details-and-trigger-payment)

## Motivation

This library is to be used by the [Payments Plugin](https://github.com/OutSystems/cordova-outsystems-payments). The repository contains a `podspec` file that is published and available on the `CocoaPods`' repository, and should be imported on the Cordova bridge as a `pod`.

## Usage

1. Include the `OSPaymentsPluginLib` pod in the Cordova Bridge. o accomplish this, the following needs to be inserted into the `plugin.xml` file. The `spec` field should be changed to the version the developer desires to use.

```xml
<platform>
	...
	<podspec>
        <config>
            <source url="https://cdn.cocoapods.org/"/>
        </config>
        <pods use-frameworks="true">
        	...
            <pod name="OSPaymentsPluginLib" spec="{VERSION TO USE}" />
            ...
        </pods>
    </podspec>
	...
</platform>
```

2. Go to [Apple Developer Portal](https://developer.apple.com/) and configure the Provisioning Profile with the `Apple Pay Payment Processing` and `In-App Purchase` capabilities enabled.

## Methods

The library provides the following methods to interact with:

### Setup Payment Configuration

```swift
func setupConfiguration()
```

Sets up the payment configuration.

The method's success is returned through a `OSPMTCallbackDelegate` call. Success operations returns an object of the structure type `OSPMTConfigurationModel`, encoded in a UTF-8 string. An `OSPMTError` error is returned in case of error.

### Check if Device is Ready for Payment

```swift
func checkWalletSetup()
```

Verifies the device is ready to process a payment, considering the configuration provided before.

The method's success is returned through a `OSPMTCallbackDelegate` call. Success operations returns an empty string or a `OSPMTError` error otherwise.

### Set Details and Trigger Payment

```swift
func set(_ details: String, and: accessToken: String?)
```

Sets payment details and triggers the request proccess. The method contains the following parameter:
- `details`: Payment details model serialized into a text field. This model can be checked in the `OSPMTDetailsModel` structure.
- `accessToken`: Authorisation token related with a full payment type. Can be empty, which should be the case for custom payments.

The method's success is returned through a `OSPMTCallbackDelegate` call. Success operations returns an object of the structure type `OSPMTScopeModel`, encoded in a UTF-8 string. An `OSPMTError` error is returned in case of error.
