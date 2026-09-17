package sqs

import (
	"fmt"
	"strconv"

	"github.com/aws/aws-sdk-go/aws"
	"github.com/aws/aws-sdk-go/aws/credentials"
	"github.com/aws/aws-sdk-go/aws/session"
	"github.com/aws/aws-sdk-go/service/sqs"
	"github.com/aws/aws-sdk-go/service/sqs/sqsiface"
	"github.com/elastic/beats/libbeat/beat"
	"github.com/elastic/beats/libbeat/logp"
	"github.com/elastic/beats/libbeat/outputs"
	"github.com/elastic/beats/libbeat/outputs/codec"
	"github.com/elastic/beats/libbeat/outputs/codec/json"
	"github.com/elastic/beats/libbeat/publisher"
)

const (
	logSelector = "sqs_output"
)

type client struct {
	svc      sqsiface.SQSAPI
	queueURL string
	beatName string
	index    string
	codec    codec.Codec
	observer outputs.Observer
}

// creates a new client using the output configuration
func newClient(config *sqsConfig, observer outputs.Observer, beat beat.Info) (*client, error) {
	sess, err := session.NewSession(&aws.Config{
		Region: &config.Region,
		Credentials: credentials.NewStaticCredentials(
			config.AccessKeyID,
			config.AccessSecretKey,
			config.AccessToken),
	})
	if err != nil {
		return nil, err
	}

	client := &client{
		svc:      sqs.New(sess),
		queueURL: config.QueueURL,
		beatName: beat.Beat,
		index:    beat.IndexPrefix,
		codec:    newCodec(beat.Version),
		observer: observer,
	}

	return client, nil
}

func newCodec(beatVersion string) *json.Encoder {
	return json.New(beatVersion, json.Config{
		Pretty:     false,
		EscapeHTML: false,
	})
}

func (c client) String() string {
	return fmt.Sprintf("sqs(%s)", c.queueURL)
}

func (c *client) Close() error {
	return nil
}

func (c *client) Connect() error {
	return nil
}

func (c *client) Publish(batch publisher.Batch) error {

	log := logp.NewLogger(logSelector)

	if c == nil {
		panic("no client")
	}
	if batch == nil {
		panic("no batch")
	}

	events := batch.Events()
	c.observer.NewBatch(len(events))

	log.Debugf("Publishing batch with %d events", len(events))

	input, err := buildInput(c, events)
	if err != nil {
		return err
	}

	output, err := c.svc.SendMessageBatch(input)
	if err != nil {
		return err
	}

	numberOfFailed := len(output.Failed)
	log.Debugf("Number of failed events: %d", numberOfFailed)
	if numberOfFailed > 0 {
		c.observer.Failed(numberOfFailed)
		batch.RetryEvents(getRetryEvents(output, events))
		return nil
	}

	batch.ACK() // if no message fails, ack the complete batch

	return nil
}

// returns the list of events to retry using the SendMessageBatchOutput
func getRetryEvents(output *sqs.SendMessageBatchOutput, events []publisher.Event) []publisher.Event {
	var retryEvents []publisher.Event
	retryEvents = make([]publisher.Event, len(output.Failed))
	for i, failed := range output.Failed {
		n, _ := strconv.Atoi(*failed.Id)
		retryEvents[i] = events[n]
	}

	return retryEvents
}

// build the input to send a batch of messages
func buildInput(c *client, events []publisher.Event) (*sqs.SendMessageBatchInput, error) {
	var entries []*sqs.SendMessageBatchRequestEntry
	entries = make([]*sqs.SendMessageBatchRequestEntry, len(events))

	for i, event := range events {
		serializedEvent, err := c.codec.Encode(c.index, &event.Content)
		if err != nil {
			return nil, err
		}

		entries[i] = &sqs.SendMessageBatchRequestEntry{
			Id:          aws.String(fmt.Sprintf("%d", i)),
			MessageBody: aws.String(string(serializedEvent)),
		}
	}

	var input = &sqs.SendMessageBatchInput{
		Entries:  entries,
		QueueUrl: &c.queueURL,
	}

	return input, nil
}
