package sqs

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestPublish(t *testing.T) {
	var batch = mockBatch{}
	var svc = mockSQS{}
	var observer = mockObserver{}

	c := client{
		beatName: "beat_name",
		codec:    newCodec("7.6"),
		index:    "index",
		observer: observer,
		queueURL: "queue_url",
		svc:      svc,
	}

	err := c.Publish(batch)

	assert.Nil(t, err)
}

func TestPublishWithError(t *testing.T) {
	var batch = mockBatch{}
	var svc = mockSQS{forceSendMessageError: true}
	var observer = mockObserver{}

	c := client{
		beatName: "beat_name",
		codec:    newCodec("7.6"),
		index:    "index",
		observer: observer,
		queueURL: "queue_url",
		svc:      svc,
	}

	err := c.Publish(batch)

	assert.NotNil(t, err)
}

func TestPublishWithFailedEvents(t *testing.T) {
	var batch = mockBatch{}
	var svc = mockSQS{forceSendMessageError: false, forceFailures: true}
	var observer = mockObserver{}

	c := client{
		beatName: "beat_name",
		codec:    newCodec("7.6"),
		index:    "index",
		observer: observer,
		queueURL: "queue_url",
		svc:      svc,
	}

	err := c.Publish(batch)

	assert.Nil(t, err)
}
