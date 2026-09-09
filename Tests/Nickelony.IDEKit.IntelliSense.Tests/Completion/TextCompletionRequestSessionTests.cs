using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Verifies the request-lifetime session the completion controller exposes: supersession, cancellation,
/// invalidation, disposal, and the safe defaults after disposal.
/// </summary>
[TestClass]
public sealed class TextCompletionRequestSessionTests
{
	[TestMethod]
	public void BeginRequest_SupersedesThePreviousRequest()
	{
		using var session = new TextCompletionRequestSession();

		long first = session.BeginRequest();
		CancellationToken firstToken = session.CurrentRequestCancellationToken;
		long second = session.BeginRequest();

		Assert.AreNotEqual(first, second);
		Assert.IsTrue(firstToken.IsCancellationRequested, "Starting a newer request must cancel the previous token.");
		Assert.IsFalse(session.IsCurrent(first));
		Assert.IsTrue(session.IsCurrent(second));
		Assert.IsFalse(session.CurrentRequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenWithoutInvalidatingTheRequest()
	{
		using var session = new TextCompletionRequestSession();

		long token = session.BeginRequest();
		session.CancelInFlightRequest();

		Assert.IsTrue(session.CurrentRequestCancellationToken.IsCancellationRequested);
		Assert.IsTrue(session.IsCurrent(token), "Cancellation is not invalidation: the identifier stays current.");
	}

	[TestMethod]
	public void InvalidateRequests_RejectsRequestsWithoutCancelingTheToken()
	{
		using var session = new TextCompletionRequestSession();

		long token = session.BeginRequest();
		CancellationToken cancellationToken = session.CurrentRequestCancellationToken;

		session.InvalidateRequests();

		Assert.IsFalse(session.IsCurrent(token));
		Assert.IsFalse(cancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CurrentRequestCancellationToken_BeforeAnyRequest_IsTheNoneToken()
	{
		using var session = new TextCompletionRequestSession();

		Assert.AreEqual(CancellationToken.None, session.CurrentRequestCancellationToken);
	}

	[TestMethod]
	public void Dispose_InvalidatesAndCancels_AndKeepsTheTokenObservable()
	{
		var session = new TextCompletionRequestSession();

		long token = session.BeginRequest();
		CancellationToken cancellationToken = session.CurrentRequestCancellationToken;

		session.Dispose();

		Assert.IsTrue(cancellationToken.IsCancellationRequested);
		Assert.AreEqual(cancellationToken, session.CurrentRequestCancellationToken);
		Assert.IsFalse(session.IsCurrent(token));
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReportSafeDefaultsAndDoNotThrow()
	{
		var session = new TextCompletionRequestSession();

		session.Dispose();
		session.Dispose();

		Assert.AreEqual(-1, session.BeginRequest());
		Assert.IsFalse(session.IsCurrent(1));

		// Both are idempotent no-ops on a disposed session.
		session.CancelInFlightRequest();
		session.InvalidateRequests();
	}

	[TestMethod]
	public void Coordinator_SharesOneLifetimeWithTheSession()
	{
		using var session = new TextCompletionRequestSession();

		long direct = session.BeginRequest();
		long viaCoordinator = session.Coordinator.BeginRequest();

		Assert.IsFalse(session.Coordinator.IsCurrent(direct));
		Assert.IsTrue(session.IsCurrent(viaCoordinator));
	}

	[TestMethod]
	public void CanPublish_RequiresCurrentAndUncanceled()
	{
		using var session = new TextCompletionRequestSession();

		long token = session.BeginRequest();
		Assert.IsTrue(session.CanPublish(token));

		session.CancelInFlightRequest();

		Assert.IsTrue(session.IsCurrent(token), "Cancellation is not invalidation.");
		Assert.IsFalse(session.CanPublish(token), "A canceled request must not be publishable.");
	}

	[TestMethod]
	public void CanPublish_SupersededAndDisposedSessions_ReportFalse()
	{
		var session = new TextCompletionRequestSession();

		long first = session.BeginRequest();
		long second = session.BeginRequest();

		Assert.IsFalse(session.CanPublish(first));
		Assert.IsTrue(session.CanPublish(second));

		session.Dispose();

		Assert.IsFalse(session.CanPublish(second));
	}

	[TestMethod]
	public void IsCurrent_NeverIssuedIdentifier_ReportsFalse()
	{
		using var session = new TextCompletionRequestSession();

		Assert.IsFalse(session.IsCurrent(0));
		Assert.IsFalse(session.IsCurrent(12345));
	}

	[TestMethod]
	public void BeginRequest_AfterInvalidateRequests_AdmitsACurrentRequestAgain()
	{
		using var session = new TextCompletionRequestSession();

		long first = session.BeginRequest();
		session.InvalidateRequests();

		Assert.IsFalse(session.IsCurrent(first));

		long second = session.BeginRequest();

		Assert.IsTrue(session.IsCurrent(second));
		Assert.IsFalse(session.IsCurrent(first));
	}

	[TestMethod]
	public void Coordinator_StaysUsableAfterDisposal_WithoutReportingCurrentThroughTheSession()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		long viaCoordinator = session.Coordinator.BeginRequest();

		Assert.AreNotEqual(0, viaCoordinator);
		Assert.IsTrue(session.Coordinator.IsCurrent(viaCoordinator));
		Assert.IsFalse(session.IsCurrent(viaCoordinator), "The disposed session's gate still rejects the identifier.");
	}

	[TestMethod]
	public void CanPublish_AfterInvalidateRequests_ReportsFalse()
	{
		using var session = new TextCompletionRequestSession();

		long token = session.BeginRequest();
		session.InvalidateRequests();

		Assert.IsFalse(session.CanPublish(token));
	}

	[TestMethod]
	public void BeginRequest_AfterDisposal_LeavesCoordinatorAdmittedRequestUntouched()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		long viaCoordinator = session.Coordinator.BeginRequest();
		CancellationToken coordinatorToken = session.Coordinator.RequestCancellationToken;

		Assert.AreEqual(-1, session.BeginRequest());
		Assert.IsTrue(
			session.Coordinator.IsCurrent(viaCoordinator),
			"A rejected admission must not supersede a request it did not admit.");
		Assert.IsFalse(coordinatorToken.IsCancellationRequested);
	}
}
