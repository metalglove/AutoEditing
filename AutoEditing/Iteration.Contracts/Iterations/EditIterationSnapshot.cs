using System;
using System.Collections.Generic;
using AutoEditing.Iteration.Contracts.Evidence;
using Core.Domain.Planning;

namespace AutoEditing.Iteration.Contracts.Iterations;

public sealed class EditIterationSnapshot
{
	public int SchemaVersion { get; set; } = ContractSchema.CurrentVersion;
	public string SessionId { get; set; } = "";
	public int Iteration { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
	public EditPlanDocument Candidate { get; set; }
	public string CandidateHash { get; set; } = "";
	public IList<EditDecisionRecord> Decisions { get; set; } = new List<EditDecisionRecord>();
	public IList<EditReviewFinding> Findings { get; set; } = new List<EditReviewFinding>();
	public IList<EditEvidenceReference> Evidence { get; set; } = new List<EditEvidenceReference>();
}
