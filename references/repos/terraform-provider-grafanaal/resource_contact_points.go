package main

import (
	"github.com/hashicorp/terraform-plugin-sdk/helper/schema"
)

func resourceContactPoints() *schema.Resource {
	return &schema.Resource{
		Create: resourceContactPointsCreate,
		Read:   resourceContactPointsRead,
		Update: resourceContactPointsUpdate,
		Delete: resourceContactPointsDelete,

		Schema: map[string]*schema.Schema{
			"disable_resolve_message": {
				Type:     schema.TypeBool,
				Optional: true,
			},
			"name": {
				Type:     schema.TypeString,
				Required: true,
			},
			"provenance": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"type": {
				Type:     schema.TypeString,
				Required: true,
			},
			"uid": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"settings": {
				Type:     schema.TypeMap,
				Required: true,
			},
		},
	}
}

func convertbody(d *schema.ResourceData) *ContactPoint {
	contactPointBody := &ContactPoint{}
	if v, ok := d.GetOk("disable_resolve_message"); ok {
		contactPointBody.DisableResolveMessage = v.(bool)
	}
	if v, ok := d.GetOk("name"); ok {
		contactPointBody.Name = v.(string)
	}
	if v, ok := d.GetOk("provenance"); ok {
		contactPointBody.Provenance = v.(string)
	}
	if v, ok := d.GetOk("type"); ok {
		contactPointBody.Type = v.(string)
	}
	if v, ok := d.GetOk("uid"); ok {
		contactPointBody.UID = v.(string)
	}
	if v, ok := d.GetOk("settings"); ok {
		contactPointBody.Settings = v.(map[string]interface{})
	}
	return contactPointBody

}

func resourceContactPointsCreate(d *schema.ResourceData, m interface{}) error {

	client := m.(*Client)

	ContactPointbody := convertbody(d)
	id, err := client.NewContactPoint(ContactPointbody)
	if err != nil {
		return err
	}
	d.SetId(id)

	return resourceContactPointsRead(d, m)

}

func resourceContactPointsRead(d *schema.ResourceData, m interface{}) error {
	client := m.(*Client)

	id := d.Id()

	contactpoint, err := client.ContactPoint(id)
	if err != nil {
		d.SetId("")
		return nil
	}
	d.SetId(contactpoint.UID)
	return nil
}

func resourceContactPointsUpdate(d *schema.ResourceData, m interface{}) error {
	client := m.(*Client)

	ContactPointbody := convertbody(d)
	err := client.UpdateContactPoint(ContactPointbody)
	if err != nil {
		return err
	}
	return resourceContactPointsRead(d, m)
}

func resourceContactPointsDelete(d *schema.ResourceData, m interface{}) error {
	client := m.(*Client)

	id := d.Id()
	err := client.DeleteContactPoint(id)
	if err != nil {
		return err
	}
	return nil
}
