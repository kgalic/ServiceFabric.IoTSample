using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IoTSample.Communication
{
    public interface IServiceBusMessageReceiver
    {
        Task ReceiveMessageAsync(Message message);
  
        Task HandleMessageReceivedFailedException(Exception e);
    }
}
