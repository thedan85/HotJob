# ML MatchScore — tính thế nào

Điểm hợp giữa một Resume và một JobPosting, thang 0–100, hiện trước nút Apply. Input chỉ lấy từ bảng có sẵn: ResumeSkill, JobSkill, JobPosting.ExperienceRequired, Application.CurrentStatus.

## 1. Feature rút từ dữ liệu

Với một cặp Resume + JobPosting:

- Skill chung: giao của ResumeSkill.SkillId và JobSkill.SkillId.
- MatchedWeightRatio = tổng ImportanceWeight của skill chung / tổng ImportanceWeight job yêu cầu.
- MatchedCountRatio = số skill chung / số skill job yêu cầu.
- ExperienceGap = tổng YearsOfExperience của Resume − JobPosting.ExperienceRequired.
- HasAllRequired = 1 nếu Resume có đủ skill job yêu cầu, ngược lại 0.

Ví dụ: job cần C# (weight 9) + SQL (weight 6). Resume chỉ có C#.
MatchedWeightRatio = 9/15 = 0.6. MatchedCountRatio = 1/2 = 0.5. HasAllRequired = 0.

## 2. Công thức nền (dùng khi chưa đủ dữ liệu train)

Khi số Application đã có kết quả dưới ~50, không train model mà tính trực tiếp:

MatchPercentage = MatchedWeightRatio × 100.

Ghi AlgorithmUsed là weighted-rule. Đủ nhanh, đủ đúng để hiện badge và sort.

Ví dụ trên cho 60%. Resume có thêm SQL 2 năm thì lên 100%.

## 3. Model ML.NET (dùng khi đủ dữ liệu)

Khi đã có nhiều Application tới trạng thái cuối, mỗi cặp từng nộp đơn thành một row train:

- Label Hired = 1 nếu Application.CurrentStatus là Offered, = 0 nếu Rejected hay Withdrawn.
- 4 feature ở mục 1 làm input.
- Pipeline: Concatenate 4 feature thành Features, trainer FastTree binary classification.
- Đánh giá lấy AccuracyScore, lưu model ra file zip, ghi một ModelTrainingLog gồm RecordCount, AccuracyScore, ModelFilePath, người train, thời điểm train.

## 4. Chấm điểm bằng model

Với cặp Resume + Job mới chưa nộp:

- Rút 4 feature như mục 1.
- Đưa qua model, lấy Probability 0–1, nhân 100 ra MatchPercentage.
- Upsert vào MatchScores theo cặp JobPostingId + ResumeId, kèm AlgorithmUsed là FastTreeBinary và CalculatedAt.
- Tính lại khi xem job có chọn Resume, hoặc khi skill đổi ở bên Resume hay bên Job.

Ví dụ: Lan mở job với Resume backend cho feature (0.9, 1.0, +1, 1) → model trả Probability 0.87 → badge hiện 87%. Đổi sang Resume frontend cho (0.1, 0.25, −1, 0) → còn 12%.
