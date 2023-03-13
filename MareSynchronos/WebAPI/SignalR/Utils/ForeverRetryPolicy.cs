﻿using MareSynchronos.Services.Mediator;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI.SignalR.Utils;

public class ForeverRetryPolicy : IRetryPolicy
{
    private readonly MareMediator _mediator;
    private bool _sentDisconnected = false;

    public ForeverRetryPolicy(MareMediator mediator)
    {
        _mediator = mediator;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        TimeSpan timeToWait = TimeSpan.FromSeconds(new Random().Next(10, 20));
        if (retryContext.PreviousRetryCount == 0)
        {
            _sentDisconnected = false;
            timeToWait = TimeSpan.FromSeconds(1);
        }
        else if (retryContext.PreviousRetryCount == 1) timeToWait = TimeSpan.FromSeconds(2);
        else if (retryContext.PreviousRetryCount == 2) timeToWait = TimeSpan.FromSeconds(3);
        else
        {
            if (!_sentDisconnected)
                _mediator.Publish(new DisconnectedMessage());
            _sentDisconnected = true;
        }

        return timeToWait;
    }
}