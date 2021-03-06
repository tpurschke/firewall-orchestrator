﻿using GraphQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FWO.ApiClient
{
    public class ApiSubscription<SubscriptionResponseType> : IDisposable
    {
        public delegate void SubscriptionUpdate(SubscriptionResponseType reponse);
        public event SubscriptionUpdate OnUpdate;

        private readonly IObservable<GraphQLResponse<dynamic>> subscriptionStream;
        private readonly IDisposable subscription;

        public ApiSubscription(IObservable<GraphQLResponse<dynamic>> subscriptionStream)
        {
            this.subscriptionStream = subscriptionStream;

            subscription = subscriptionStream.Subscribe(response =>
            {
                JsonElement.ObjectEnumerator responseObjectEnumerator = response.Data.EnumerateObject();
                responseObjectEnumerator.MoveNext();
                SubscriptionResponseType returnValue = JsonSerializer.Deserialize<SubscriptionResponseType>(responseObjectEnumerator.Current.Value.GetRawText());
                OnUpdate(returnValue);
            });
        }

        public void Dispose()
        {
            subscription.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
