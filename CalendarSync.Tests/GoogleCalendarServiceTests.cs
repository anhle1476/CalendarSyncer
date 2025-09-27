using CalendarSync.Models;
using CalendarSync.Services;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CalendarSync.Tests
{
    public class GoogleCalendarServiceTests
    {
        private Mock<ILogger<GoogleCalendarService>> _loggerMock;
        private Mock<ICalendarWrapper> _calendarWrapperMock;
        private GoogleCalendarService _googleCalendarService;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<GoogleCalendarService>>();
            _calendarWrapperMock = new Mock<ICalendarWrapper>();

            _googleCalendarService = new GoogleCalendarService(
                _loggerMock.Object,
                _calendarWrapperMock.Object
            );
        }

        [Test]
        public async Task GetEventsAsync_ReturnsEvents_WhenApiCallIsSuccessful()
        {
            // Arrange
            var events = new Events
            {
                Items = new List<Event> { new Event { Summary = "Test Event" } }
            };

            _calendarWrapperMock.Setup(x => x.ListEventsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(events);

            // Act
            var result = await _googleCalendarService.GetEventsAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Summary, Is.EqualTo("Test Event"));
        }

        [Test]
        public async Task GetEventsAsync_ReturnsNull_WhenApiCallFails()
        {
            // Arrange
            _calendarWrapperMock.Setup(x => x.ListEventsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("API Error"));

            // Act
            var result = await _googleCalendarService.GetEventsAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.Null);
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to retrieve events.")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
        }
    }
}