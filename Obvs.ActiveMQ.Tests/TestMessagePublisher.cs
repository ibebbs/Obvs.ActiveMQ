﻿using System;
using System.Collections.Generic;
using System.Threading;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Apache.NMS.ActiveMQ.Commands;
using FakeItEasy;
using NUnit.Framework;
using Obvs.Serialization.Json;
using Obvs.Types;
using IMessage = Obvs.Types.IMessage;

namespace Obvs.ActiveMQ.Tests
{
    [TestFixture]
    public class TestMessagePublisher
    {
        private IConnectionFactory _connectionFactory;
        private IConnection _connection;
        private ISession _session;
        private IMessageProducer _producer;
        private IMessageSerializer _serializer;
        private IMessagePublisher<IMessage> _publisher;
        private IDestination _destination;
        private ITextMessage _message;
        private IMessagePropertyProvider<IMessage> _messagePropertyProvider;

        private interface ITestMessage : IEvent
        {
        }

        private interface ITestMessage2 : IEvent
        {
        }

        private class TestMessage : ITestMessage
        {
            public int Id { get; set; }

            public override string ToString()
            {
                return "TestMessage " + Id;
            }
        }

        private class TestMessage2 : ITestMessage2
        {
            public int Id { get; set; }

            public override string ToString()
            {
                return "TestMessage2 " + Id;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _connectionFactory = A.Fake<IConnectionFactory>();
            _connection = A.Fake<IConnection>();
            _session = A.Fake<ISession>();
            _producer = A.Fake<IMessageProducer>();
            _serializer = A.Fake<IMessageSerializer>();
            _destination = A.Fake<IDestination>();
            _message = A.Fake<ITextMessage>();
            _messagePropertyProvider = A.Fake<IMessagePropertyProvider<IMessage>>();

            A.CallTo(() => _connectionFactory.CreateConnection()).Returns(_connection);
            A.CallTo(() => _connection.CreateSession(A<AcknowledgementMode>.Ignored)).Returns(_session);
            A.CallTo(() => _session.CreateProducer(_destination)).Returns(_producer);
            A.CallTo(() => _session.CreateTextMessage(A<string>._)).Returns(_message);
            A.CallTo(() => _serializer.Serialize(A<object>._)).Returns("SerializedString");

            _publisher = new MessagePublisher<IMessage>(_connectionFactory, _destination, _serializer, _messagePropertyProvider);
        }

        [Test]
        public void ShouldConnectToBrokerOnceOnFirstPublish()
        {
            _publisher.Publish(new TestMessage());

            A.CallTo(() => _connectionFactory.CreateConnection()).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _session.CreateProducer(_destination)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _connection.Start()).MustHaveHappened(Repeated.Exactly.Once);

            _publisher.Publish(new TestMessage());
            _publisher.Publish(new TestMessage());

            A.CallTo(() => _connectionFactory.CreateConnection()).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Test]
        public void ShouldSendMessageWithPropertiesWhenPublishing()
        {
            ITestMessage testMessage = new TestMessage();
            A.CallTo(() => _messagePropertyProvider.GetProperties(testMessage)).Returns(new Dictionary<string, object> { { "key1", 1 }, { "key2", "2" }, { "key3", 3.0 }, { "key4", 4L }, { "key5", true } });

            _publisher.Publish(testMessage);

            A.CallTo(() => _producer.Send(_message)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetInt("key1", 1)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetString("key2", "2")).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetDouble("key3", 3.0)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetLong("key4", 4L)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetBool("key5", true)).MustHaveHappened(Repeated.Exactly.Once);
        }
        
        [Test]
        public void ShouldSendBytesMessageSerializerReturnsBytes()
        {
            ITestMessage testMessage = new TestMessage();
            byte[] bytes = new byte[0];

            IBytesMessage bytesMessage = A.Fake<IBytesMessage>();
            A.CallTo(() => _serializer.Serialize(testMessage)).Returns(bytes);
            A.CallTo(() => _session.CreateBytesMessage(bytes)).Returns(bytesMessage);

            _publisher.Publish(testMessage);

            A.CallTo(() => _producer.Send(bytesMessage)).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Test]
        public void ShouldSendMessageWithTypeNamePropertySet()
        {
            ITestMessage testMessage = new TestMessage();
            A.CallTo(() => _messagePropertyProvider.GetProperties(testMessage)).Returns(new Dictionary<string, object>());

            _publisher.Publish(testMessage);

            A.CallTo(() => _producer.Send(_message)).MustHaveHappened(Repeated.Exactly.Once);
            A.CallTo(() => _message.Properties.SetString(MessagePropertyNames.TypeName, typeof(TestMessage).Name)).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Test]
        public void ShouldDisconnectWhenDisposed()
        {
            _publisher.Publish(new TestMessage());
            _publisher.Dispose();

            A.CallTo(() => _connection.Close()).MustHaveHappened(Repeated.Exactly.Once);
        }

        [Test, Explicit]
        public void ShouldCorrectlyPublishAndSubscribeToMulipleMultiplexedTopics()
        {
            const string brokerUri = "<insert test broker uri here>";
            const string topicName1 = "Obvs.Tests.ShouldCorrectlyPublishAndSubscribeToMulipleMultiplexedTopics1";
            const string topicName2 = "Obvs.Tests.ShouldCorrectlyPublishAndSubscribeToMulipleMultiplexedTopics2";

            IMessagePropertyProvider<IMessage> getProperties = new DefaultPropertyProvider<IMessage>();

            IConnectionFactory connectionFactory = new ConnectionFactory(brokerUri);

            IMessagePublisher<IMessage> publisher1 = new MessagePublisher<IMessage>(
                connectionFactory,
                new ActiveMQTopic(topicName1),
                new JsonMessageSerializer(),
                getProperties);

            IMessagePublisher<IMessage> publisher2 = new MessagePublisher<IMessage>(
                connectionFactory,
                new ActiveMQTopic(topicName2),
                new JsonMessageSerializer(),
                getProperties);

            IMessageDeserializer<IMessage>[] deserializers =
            {
                new JsonMessageDeserializer<TestMessage>(),
                new JsonMessageDeserializer<TestMessage2>()
            };

            IMessageSource<IMessage> source = new MergedMessageSource<IMessage>(new[]
            {
                new MessageSource<IMessage>(
                    connectionFactory,
                    deserializers,
                    new ActiveMQTopic(topicName1),
                    AcknowledgementMode.AutoAcknowledge),

                new MessageSource<IMessage>(
                    connectionFactory,
                    deserializers,
                    new ActiveMQTopic(topicName2),
                    AcknowledgementMode.AutoAcknowledge)
            });

            source.Messages.Subscribe(Console.WriteLine);

            publisher1.Publish(new TestMessage { Id = 1234 });
            publisher1.Publish(new TestMessage2 { Id = 4567 });
            publisher2.Publish(new TestMessage { Id = 8910 });
            publisher2.Publish(new TestMessage2 { Id = 1112 });

            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }
}
