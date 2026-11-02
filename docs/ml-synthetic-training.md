# Synthetic match-model training (dev only)

Seeds deterministic demo applications and trains the ML.NET FastTree match model. This is for verifying the pipeline, not for real hiring prediction.

## Guardrails

- Only runs when `ASPNETCORE_ENVIRONMENT=Development`.
- Only runs when `DevTools:SyntheticTrainingEnabled=true` (env var: `DevTools__SyntheticTrainingEnabled=true`).
- Requires the `Admin` role.
- Otherwise the endpoint returns `404`.
- Synthetic rows use `ml-demo-candidate-*@example.invalid` and are reused on rerun.
- It selects four non-closed jobs with skills, preferring published jobs, and creates 14 candidates × 4 applications:
  - 6 strong profiles → 24 `Offered`
  - 4 zero-experience profiles → 16 `Rejected`
  - 4 partial-skill profiles → 16 `Withdrawn`
- Existing finished applications are included in the training rows.

## Run

```bash
cd HotJob/hotjob
DevTools__SyntheticTrainingEnabled=true \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --urls http://localhost:5168
```

Log in as an admin in Swagger, then run:

```http
POST /api/dev/match-model/seed-and-train
Authorization: Bearer <admin access token>
```

The response contains candidate/application counts and the existing `MatchTrainingResultDto`.

## Verify

```bash
mysql -uroot hotjob -e "
SELECT Id,RecordCount,AccuracyScore,IsActive,ModelFilePath FROM ModelTrainingLogs ORDER BY Id;
SELECT CurrentStatus,COUNT(*) FROM Applications a
JOIN AspNetUsers u ON u.Id=(SELECT UserId FROM Candidates c WHERE c.Id=a.CandidateId)
WHERE u.Email LIKE 'ml-demo-candidate-%' GROUP BY CurrentStatus;
"
ls -l HotJob/hotjob/App_Data/models/matchscore.zip
```

Then open a job detail page as a candidate. The match line should say:

```text
Calculated with the trained ML model (FastTreeBinary).
```

## Notes

- Training needs at least `50` finished applications with at least `5` per outcome.
- A `Rejected` application whose latest note is exactly `Job closed` is excluded.
- The model is promoted only when its hold-out accuracy is at least the active model's.
- Model file: `HotJob/hotjob/App_Data/models/matchscore.zip` (gitignored).
- Rerun the endpoint to confirm no duplicate candidates or applications.
