using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IoTSample.Communication
{
    public class ServiceBusCommunicationClientFactory : CommunicationClientFactoryBase<ServiceBusCommunicationClient>
    {
        #region Fields

        private string _queueName;

        #endregion

        #region Constructors 

        public ServiceBusCommunicationClientFactory(ServicePartitionResolver servicePartitionResolver,
                                                    string queueName) : base(servicePartitionResolver)
        {
            _queueName = queueName;
        }

        #endregion

        #region Overrides 

        protected override async void AbortClient(ServiceBusCommunicationClient client)
        {
            await client.CloseClientAsync();
        }

        protected override Task<ServiceBusCommunicationClient> CreateClientAsync(string endpoint, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ServiceBusCommunicationClient(endpoint, _queueName));
        }

        protected override Task OpenClient(ServiceBusCommunicationClient client, CancellationToken cancellationToken)
        {
            client.OpenClient();
            return Task.FromResult(true);
        }

        protected override bool ValidateClient(ServiceBusCommunicationClient client)
        {
            return client != null;
        }

        protected override bool ValidateClient(string endpoint, ServiceBusCommunicationClient client)
        {
            return client != null && !string.IsNullOrWhiteSpace(endpoint);
        }

        #endregion
    }
}
