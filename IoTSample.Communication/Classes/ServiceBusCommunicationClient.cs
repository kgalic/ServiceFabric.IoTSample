using Microsoft.Azure.ServiceBus;
using Microsoft.ServiceFabric.Services.Communication.Client;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Text;
using System.Threading.Tasks;

namespace IoTSample.Communication
{
    public class ServiceBusCommunicationClient : ICommunicationClient
    {
        #region Fields

        private QueueClient _queueClient;
        private string _queueName;
        private string _connectionString;

        #endregion

        #region Overrides

        public ResolvedServicePartition ResolvedServicePartition { get; set; }

        public string ListenerName { get; set; }

        public ResolvedServiceEndpoint Endpoint { get; set; }

        #endregion

        #region Constructors 

        public ServiceBusCommunicationClient(string connectionString, string queueName)
        {
            _connectionString = connectionString;
            _queueName = queueName;
        }

        #endregion

        #region Public

        public void OpenClient()
        {
            if (!string.IsNullOrEmpty(_connectionString) && !string.IsNullOrEmpty(_queueName))
            {
                _queueClient = new QueueClient(_connectionString, _queueName);
            }
        }

        public Task SendMessageAsync(Message message)
        {
            return _queueClient.SendAsync(message);
        }

        public Task CloseClientAsync()
        {
            return _queueClient.CloseAsync();
        }

        #endregion
    }
}
