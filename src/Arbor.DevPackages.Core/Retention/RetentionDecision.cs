namespace Arbor.DevPackages.Core.Retention;

public enum RetentionAction { Keep, Purge }

public sealed record RetentionDecision(RetentionAction Action, string Reason);
