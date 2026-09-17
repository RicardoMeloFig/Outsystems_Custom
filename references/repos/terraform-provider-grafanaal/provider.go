package main

import (
	"github.com/hashicorp/terraform-plugin-sdk/helper/schema"
)

func Provider() *schema.Provider {
	return &schema.Provider{

		Schema: map[string]*schema.Schema{
			"url": {
				Type:     schema.TypeString,
				Required: true,
			},
			"token": {
				Type:     schema.TypeString,
				Required: true,
			},
		},

		ResourcesMap: map[string]*schema.Resource{
			"grafanaal_rule":                resourceAlertRule(),
			"grafanaal_contact_point":       resourceContactPoints(),
			"grafanaal_notification_policy": resourceNotificationPolicy(),
		},
		ConfigureFunc: providerConfigure,
	}
}

func providerConfigure(d *schema.ResourceData) (interface{}, error) {
	url, _ := d.Get("url").(string)
	token, _ := d.Get("token").(string)

	client, err := NewClient(url, token)
	return client, err
}
