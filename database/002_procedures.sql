CREATE OR ALTER PROCEDURE nlp.GetRequesterIntent @MemberId varchar(64), @IntentId varchar(64), @ContextId varchar(64) AS BEGIN SET NOCOUNT ON; SELECT TOP (1) * FROM nlp.NlpIntent WHERE intent_id=@IntentId AND member_id=@MemberId AND context_id=@ContextId AND status='ACTIVE' AND expires_at>SYSUTCDATETIME(); END;
GO
CREATE OR ALTER PROCEDURE nlp.GetEligibleCandidates @RequesterId varchar(64), @ContextId varchar(64), @MaxRows int=200 AS BEGIN SET NOCOUNT ON; SELECT TOP (@MaxRows) * FROM nlp.NlpIntent WHERE member_id<>@RequesterId AND context_id=@ContextId AND status='ACTIVE' AND expires_at>SYSUTCDATETIME() ORDER BY expires_at DESC; END;
GO
CREATE OR ALTER PROCEDURE nlp.SaveMatchResults @RequestId varchar(64), @RequesterId varchar(64), @ResultsJson nvarchar(max) AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM nlp.NlpMatchResult WHERE request_id=@RequestId) RETURN; -- Application owns validated JSON projection in V1. END;
GO
CREATE OR ALTER PROCEDURE nlp.SaveFeedback @RequestId varchar(64), @RequesterId varchar(64), @CandidateId varchar(64), @Label varchar(64), @Reason nvarchar(1000)=NULL AS BEGIN SET NOCOUNT ON; INSERT nlp.NlpFeedback(request_id,requester_id,candidate_id,label,reason) VALUES(@RequestId,@RequesterId,@CandidateId,@Label,@Reason); END;
GO
