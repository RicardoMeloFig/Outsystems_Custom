package main

import (
	"encoding/json"
	"strconv"

	"github.com/hashicorp/terraform-plugin-sdk/helper/schema"
)

func resourceNotificationPolicy() *schema.Resource {
	return &schema.Resource{
		Create: resourceNotificationPolicyCreate,
		Read:   resourceNotificationPolicyRead,
		Update: resourceNotificationPolicyUpdate,
		Delete: resourceNotificationPolicyDelete,

		Schema: map[string]*schema.Schema{
			"continue": {
				Type:     schema.TypeBool,
				Optional: true,
			},
			"group_by_str": {
				Type:     schema.TypeList,
				Optional: true,
				Elem: &schema.Schema{
					Type: schema.TypeString,
				},
			},
			"mute_time_intervals": {
				Type:     schema.TypeString,
				Optional: true,
				Elem: &schema.Schema{
					Type: schema.TypeString,
				},
			},
			"receiver": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"routes": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"group_interval": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"group_wait": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"object_matchers": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"provenance": {
				Type:     schema.TypeString,
				Optional: true,
			},
			"repeat_interval": {
				Type:     schema.TypeString,
				Optional: true,
			},
		},
	}
}

func convertNP(d *schema.ResourceData) *SpecificPolicy {
	npbody := &SpecificPolicy{}

	if v, ok := d.GetOk("receiver"); ok {
		npbody.Receiver = v.(string)
	}
	if v, ok := d.GetOk("group_by_str"); ok {
		npbody.GroupBy = v.([]string)
	}
	if v, ok := d.GetOk("object_matchers"); ok {

		var jsonMap map[string]interface{}
		json.Unmarshal([]byte(v.(string)), &jsonMap)
		npbody.ObjectMatchers = jsonMap
	}
	if v, ok := d.GetOk("mute_time_intervals"); ok {
		npbody.MuteTimeIntervals = v.([]string)
	}
	if v, ok := d.GetOk("continue"); ok {
		npbody.Continue = v.(bool)
	}
	if v, ok := d.GetOk("provenance"); ok {
		npbody.Provenance = v.(string)
	}
	if v, ok := d.GetOk("group_interval"); ok {
		converted_int, err := strconv.ParseInt(v.(string), 10, 64)
		if err != nil {
			panic(err)
		}
		npbody.GroupWait = converted_int
	}
	if v, ok := d.GetOk("group_wait"); ok {
		converted_int, err := strconv.ParseInt(v.(string), 10, 64)
		if err != nil {
			panic(err)
		}
		npbody.GroupInterval = converted_int
	}
	if v, ok := d.GetOk("repeat_interval"); ok {
		converted_int, err := strconv.ParseInt(v.(string), 10, 64)
		if err != nil {
			panic(err)
		}
		npbody.RepeatInterval = converted_int
	}
	if v, ok := d.GetOk("routes"); ok {
		var jsonMap map[string]interface{}
		json.Unmarshal([]byte(v.(string)), &jsonMap)

		npbody.Routes = jsonMap
	}

	return npbody
}

func resourceNotificationPolicyCreate(d *schema.ResourceData, m interface{}) error {

	client := m.(*Client)

	npbody := convertNP(d)
	err := client.SetNotificationPolicyTree(npbody)
	if err != nil {
		return err
	}

	return resourceNotificationPolicyRead(d, m)
}

func resourceNotificationPolicyRead(d *schema.ResourceData, m interface{}) error {
	return nil
}

func resourceNotificationPolicyUpdate(d *schema.ResourceData, m interface{}) error {
	client := m.(*Client)

	npbody := convertNP(d)
	err := client.SetNotificationPolicyTree(npbody)
	if err != nil {
		return err
	}

	return resourceNotificationPolicyRead(d, m)
}

func resourceNotificationPolicyDelete(d *schema.ResourceData, m interface{}) error {

	client := m.(*Client)

	err := client.ResetNotificationPolicyTree()
	if err != nil {
		return err
	}
	return nil
}
