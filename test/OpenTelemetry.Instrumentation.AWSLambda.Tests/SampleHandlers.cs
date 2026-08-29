// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using Amazon.Lambda.SQSEvents;

namespace OpenTelemetry.Instrumentation.AWSLambda.Tests;

internal class SampleHandlers
{
    /// <summary>
    /// Gets the baggage observed by the most recently invoked baggage-capturing handler.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>>? ObservedBaggage { get; private set; }

    // Action<TInput, ILambdaContext>
#pragma warning disable SA1313
    public void SampleHandlerSyncInputAndNoReturn(string _1, ILambdaContext _2)
    {
    }

    // Records the baggage visible inside the handler for an SQS event.
    public void SampleHandlerSyncSqsEventCapturingBaggage(SQSEvent _1, ILambdaContext _2)
        => this.ObservedBaggage = Baggage.Current.GetBaggage();

    // Records the baggage visible inside the handler for an API Gateway request.
    public void SampleHandlerSyncApiGatewayCapturingBaggage(APIGatewayProxyRequest _1, ILambdaContext _2)
        => this.ObservedBaggage = Baggage.Current.GetBaggage();

    // Records the baggage visible inside an async handler for an SQS event.
    public async Task SampleHandlerAsyncSqsEventCapturingBaggage(SQSEvent _1, ILambdaContext _2)
    {
        await Task.Delay(10);
        this.ObservedBaggage = Baggage.Current.GetBaggage();
    }

    // Records the baggage visible inside the handler and then throws.
    public void SampleHandlerSyncSqsEventCapturingBaggageThenThrowing(SQSEvent _1, ILambdaContext _2)
    {
        this.ObservedBaggage = Baggage.Current.GetBaggage();
        throw new InvalidOperationException("Handler failure after observing baggage.");
    }

    // Func<TInput, ILambdaContext, TResult>
    public string SampleHandlerSyncInputAndReturn(string str, ILambdaContext _2)
    {
        return str;
    }

    // Action<TInput, ILambdaContext> for an SQS event source.
    public void SampleHandlerSyncSqsEvent(SQSEvent _1, ILambdaContext _2)
    {
    }

    // Action<TInput, ILambdaContext> for a single SQS message.
    public void SampleHandlerSyncSqsMessage(SQSEvent.SQSMessage _1, ILambdaContext _2)
    {
    }

    // Action<TInput, ILambdaContext> for an SNS event source.
    public void SampleHandlerSyncSnsEvent(SNSEvent _1, ILambdaContext _2)
    {
    }

    // Action<TInput, ILambdaContext> for a single SNS record.
    public void SampleHandlerSyncSnsRecord(SNSEvent.SNSRecord _1, ILambdaContext _2)
    {
    }

    // Func<TInput, ILambdaContext, Task>
    public async Task SampleHandlerAsyncInputAndNoReturn(string _1, ILambdaContext _2)
    {
        await Task.Delay(10);
    }

    // Func<TInput, ILambdaContext, Task<TResult>>
    public async Task<string> SampleHandlerAsyncInputAndReturn(string str, ILambdaContext _2)
    {
        await Task.Delay(10);
        return str;
    }

    // Action<TInput, ILambdaContext>
    public void SampleHandlerSyncNoReturnException(string str, ILambdaContext _2)
#pragma warning restore SA1313
    {
        throw new Exception(str);
    }
}
