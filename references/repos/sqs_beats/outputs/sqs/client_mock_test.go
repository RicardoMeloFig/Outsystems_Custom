package sqs

import (
	"errors"

	"github.com/aws/aws-sdk-go/aws"
	"github.com/aws/aws-sdk-go/service/sqs"
	"github.com/aws/aws-sdk-go/service/sqs/sqsiface"
	"github.com/elastic/beats/libbeat/outputs"
	"github.com/elastic/beats/libbeat/publisher"
)

type mockBatch struct {
	publisher.Batch
}

func (b mockBatch) Events() []publisher.Event {
	return make([]publisher.Event, 1)
}

func (b mockBatch) ACK() {
}

func (b mockBatch) RetryEvents(events []publisher.Event) {
}

type mockSQS struct {
	sqsiface.SQSAPI

	forceSendMessageError bool
	forceFailures         bool
}

func (svc mockSQS) SendMessageBatch(*sqs.SendMessageBatchInput) (*sqs.SendMessageBatchOutput, error) {

	if svc.forceSendMessageError {
		return nil, errors.New("error_send_message")
	}

	if svc.forceFailures {
		entries := make([]*sqs.BatchResultErrorEntry, 1)
		entries[0] = &sqs.BatchResultErrorEntry{Id: aws.String("0")}
		output := sqs.SendMessageBatchOutput{
			Failed:     entries,
			Successful: make([]*sqs.SendMessageBatchResultEntry, 0),
		}

		return &output, nil

	}

	output := sqs.SendMessageBatchOutput{
		Successful: make([]*sqs.SendMessageBatchResultEntry, 1),
	}

	return &output, nil
}

type mockObserver struct {
	outputs.Observer
}

func (o mockObserver) NewBatch(numberOfEvents int) {
}

func (o mockObserver) Failed(numberOfEvents int) {
}
