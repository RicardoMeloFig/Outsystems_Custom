package sqs

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestValidateConfig(t *testing.T) {
	tests := []struct {
		Name  string
		Input sqsConfig
		Valid bool
	}{
		{"No config", sqsConfig{}, false},
		{"Just access_key", sqsConfig{AccessKeyID: "accesskey"}, false},
		{"Just region", sqsConfig{Region: "eu-west-1"}, false},
		{"Just queue_name", sqsConfig{QueueURL: "test_queue"}, false},
		{"Just region and queue_name", sqsConfig{QueueURL: "test_queue", Region: "eu-west-1"}, false},
		{"With all required", sqsConfig{QueueURL: "test_queue", Region: "eu-west-1", BatchSize: 1}, true},
		{"With access_key and secret_key and all required ", sqsConfig{AccessKeyID: "accesskey", AccessSecretKey: "secretkey", QueueURL: "test_queue", Region: "eu-west-1", BatchSize: 1}, true},
		{"With access_key and all required ", sqsConfig{AccessKeyID: "accesskey", QueueURL: "test_queue", Region: "eu-west-1", BatchSize: 1}, false},
	}

	for _, test := range tests {
		assert.Equal(t, test.Valid, test.Input.Validate() == nil, test.Name)
	}
}
