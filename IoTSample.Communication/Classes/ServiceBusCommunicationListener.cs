using Microsoft.Azure.ServiceBus;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IoTSample.Communication
{
    public class ServiceBusCommunicationListener : ICommunicationListener
    {
        #region Fields

        private QueueClient _queueClient;
        private string _connectionString;
        private string _queueName;

        private IServiceBusMessageReceiver _serviceBusMessageReceiver;

        #endregion

        #region Constructors

        public ServiceBusCommunicationListener(IServiceBusMessageReceiver serviceBusMessageReceiver, 
                                               string connectionString, 
                                               string queueName) 
        {
            _serviceBusMessageReceiver = serviceBusMessageReceiver;
            _connectionString = connectionString;
            _queueName = queueName;
        }

        #endregion

        #region Overrides

        public void Abort()
        {

        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            return _queueClient.CloseAsync();
        }

        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            InitializeQueueClient();

            if (_queueClient == null)
            {
                throw new InvalidOperationException();
            }

            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedAsync);
            _queueClient.RegisterMessageHandler(ReceiveMessageAsync, messageHandlerOptions);
            return Task.FromResult(_connectionString);
        }

        #endregion

        #region Private

        private void InitializeQueueClient()
        {
            if (!string.IsNullOrEmpty(_connectionString) && !string.IsNullOrEmpty(_queueName))
            {
                _queueClient = new QueueClient(_connectionString, _queueName);
            }
        }

        private Task ReceiveMessageAsync(Message message, CancellationToken token)
        {
            return _serviceBusMessageReceiver.ReceiveMessageAsync(message);
        }

        private Task ExceptionReceivedAsync(ExceptionReceivedEventArgs args)
        {
            return _serviceBusMessageReceiver.HandleMessageReceivedFailedException(args.Exception);
        }

        #endregion

    }
}
