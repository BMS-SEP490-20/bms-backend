// seed_rich_medical_packages.js
// Seeds comprehensive DIAG-standard test packages and biomarkers into MongoDB.

const db = db.getSiblingDB("BMS_Database");

print("=== STARTING RICH MEDICAL PACKAGES & BIOMARKERS SEEDING ===");

// 1. MASTER TEST CRITERIA
const criteriaToUpsert = [
  // Huyết học (Hematology)
  { code: "RBC", name: "Hồng cầu (Red Blood Cells)", category: "Huyết học", unit: "T/L", sample_type: "Máu toàn phần EDTA", reference_range: "4.0 – 5.5", price: 30000 },
  { code: "HGB", name: "Huyết sắc tố (Hemoglobin)", category: "Huyết học", unit: "g/L", sample_type: "Máu toàn phần EDTA", reference_range: "120 – 160", price: 30000 },
  { code: "HCT", name: "Dung tích hồng cầu (Hematocrit)", category: "Huyết học", unit: "%", sample_type: "Máu toàn phần EDTA", reference_range: "37 – 48", price: 25000 },
  { code: "MCV", name: "Thể tích trung bình hồng cầu (MCV)", category: "Huyết học", unit: "fL", sample_type: "Máu toàn phần EDTA", reference_range: "80 – 100", price: 25000 },
  { code: "MCH", name: "Lượng Hb trung bình hồng cầu (MCH)", category: "Huyết học", unit: "pg", sample_type: "Máu toàn phần EDTA", reference_range: "27 – 32", price: 25000 },
  { code: "MCHC", name: "Nồng độ Hb trung bình hồng cầu (MCHC)", category: "Huyết học", unit: "g/L", sample_type: "Máu toàn phần EDTA", reference_range: "320 – 360", price: 25000 },
  { code: "RDW", name: "Dải phân bố kích thước hồng cầu (RDW-CV)", category: "Huyết học", unit: "%", sample_type: "Máu toàn phần EDTA", reference_range: "11.5 – 14.5", price: 25000 },
  { code: "WBC", name: "Bạch cầu (White Blood Cells)", category: "Huyết học", unit: "G/L", sample_type: "Máu toàn phần EDTA", reference_range: "4.0 – 10.0", price: 30000 },
  { code: "PLT", name: "Tiểu cầu (Platelet)", category: "Huyết học", unit: "G/L", sample_type: "Máu toàn phần EDTA", reference_range: "150 – 450", price: 30000 },
  { code: "CBC", name: "Tổng phân tích tế bào máu ngoại vi (24 thông số Laser)", category: "Huyết học", unit: "Bản ghi 24 chỉ số", sample_type: "Máu toàn phần EDTA", reference_range: "Theo từng chỉ số", price: 98000 },
  { code: "RETIC", name: "Đếm hồng cầu lưới (Reticulocytes)", category: "Huyết học", unit: "%", sample_type: "Máu toàn phần EDTA", reference_range: "0.5 – 1.5", price: 104000 },
  { code: "HGB_ELEC", name: "Điện di huyết sắc tố (Hb Electrophoresis)", category: "Huyết học", unit: "%", sample_type: "Máu toàn phần EDTA", reference_range: "HbA: 95-98%, HbA2: 2.0-3.3%", price: 268000 },

  // Dinh dưỡng & Dự trữ sắt (Anemia / Iron profile)
  { code: "FERRITIN", name: "Định lượng Ferritin huyết thanh", category: "Miễn dịch", unit: "ng/mL", sample_type: "Huyết thanh", reference_range: "30 – 400 (Nam), 15 – 150 (Nữ)", price: 139000 },
  { code: "TRANSFERRIN", name: "Định lượng Transferrin", category: "Sinh hóa", unit: "g/L", sample_type: "Huyết thanh", reference_range: "2.0 – 3.6", price: 68000 },
  { code: "IRON", name: "Định lượng Sắt huyết thanh (Serum Iron)", category: "Sinh hóa", unit: "umol/L", sample_type: "Huyết thanh", reference_range: "11 – 30", price: 75000 },
  { code: "TIBC", name: "Khả năng gắn sắt toàn phần (TIBC)", category: "Sinh hóa", unit: "umol/L", sample_type: "Huyết thanh", reference_range: "45 – 72", price: 85000 },
  { code: "B12", name: "Định lượng Vitamin B12 huyết thanh", category: "Miễn dịch", unit: "pg/mL", sample_type: "Huyết thanh", reference_range: "200 – 900", price: 173000 },
  { code: "FOLATE", name: "Định lượng Vitamin B9 (Axit Folic / Folate)", category: "Miễn dịch", unit: "ng/mL", sample_type: "Huyết thanh", reference_range: "> 4.0", price: 190000 },

  // Sinh hóa Gan - Mật - Thụy
  { code: "ALT", name: "Men gan ALT (GPT)", category: "Sinh hóa", unit: "U/L", sample_type: "Huyết thanh", reference_range: "< 35", price: 45000 },
  { code: "AST", name: "Men gan AST (GOT)", category: "Sinh hóa", unit: "U/L", sample_type: "Huyết thanh", reference_range: "< 35", price: 45000 },
  { code: "GGT", name: "Men gan GGT (Gamma GT)", category: "Sinh hóa", unit: "U/L", sample_type: "Huyết thanh", reference_range: "9 – 48", price: 65000 },
  { code: "BILI_TOT", name: "Bilirubin toàn phần", category: "Sinh hóa", unit: "umol/L", sample_type: "Huyết thanh", reference_range: "< 17.1", price: 50000 },
  { code: "ALB", name: "Albumin huyết thanh", category: "Sinh hóa", unit: "g/L", sample_type: "Huyết thanh", reference_range: "35 – 52", price: 45000 },

  // Đường huyết & Thận (Diabetes & Renal)
  { code: "GLU", name: "Đường huyết đói (Glucose)", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết tương NaF / Huyết thanh", reference_range: "3.9 – 6.4", price: 40000 },
  { code: "HBA1C", name: "Hemoglobin A1c (HbA1c HPLC)", category: "Sinh hóa", unit: "%", sample_type: "Máu toàn phần EDTA", reference_range: "< 5.7", price: 180000 },
  { code: "INSULIN", name: "Định lượng Insulin máu", category: "Miễn dịch", unit: "uIU/mL", sample_type: "Huyết thanh", reference_range: "2.6 – 24.9", price: 150000 },
  { code: "URE", name: "Ure huyết thanh", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: "2.5 – 7.5", price: 40000 },
  { code: "CRE", name: "Creatinine máu & eGFR", category: "Sinh hóa", unit: "umol/L", sample_type: "Huyết thanh", reference_range: "53 – 106", price: 50000 },
  { code: "UA", name: "Acid Uric huyết thanh", category: "Sinh hóa", unit: "umol/L", sample_type: "Huyết thanh", reference_range: "200 – 420 (Nam), 140 – 340 (Nữ)", price: 45000 },
  { code: "ELEC", name: "Điện giải đồ (Na+, K+, Cl-)", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: "Na: 135-145, K: 3.5-5.0, Cl: 98-106", price: 75000 },
  { code: "URINE_10", name: "Tổng phân tích nước tiểu 10 thông số", category: "Sinh hóa", unit: "10 thông số", sample_type: "Nước tiểu", reference_range: "Âm tính các chỉ số bất thường", price: 55000 },

  // Mỡ máu (Lipid Profile)
  { code: "CHO", name: "Cholesterol toàn phần", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: "< 5.2", price: 45000 },
  { code: "TRI", name: "Triglyceride máu", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: "< 1.7", price: 45000 },
  { code: "HDL", name: "HDL-Cholesterol (Mỡ tốt)", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: ">= 1.0", price: 55000 },
  { code: "LDL", name: "LDL-Cholesterol trực tiếp (Mỡ xấu)", category: "Sinh hóa", unit: "mmol/L", sample_type: "Huyết thanh", reference_range: "< 3.4", price: 55000 },

  // Đông máu (Coagulation)
  { code: "PT_SEC", name: "Thời gian Prothrombin (PT giây)", category: "Đông máu", unit: "s", sample_type: "Huyết tương Citrate", reference_range: "11.0 – 14.0", price: 70000 },
  { code: "PT_INR", name: "Chỉ số bình chuẩn quốc tế INR", category: "Đông máu", unit: "Chỉ số", sample_type: "Huyết tương Citrate", reference_range: "0.8 – 1.2", price: 60000 },
  { code: "APTT", name: "Thời gian Thromboplastin từng phần hoạt hóa (APTT)", category: "Đông máu", unit: "s", sample_type: "Huyết tương Citrate", reference_range: "26.0 – 38.0", price: 70000 },
  { code: "FIBRIN", name: "Định lượng Fibrinogen", category: "Đông máu", unit: "g/L", sample_type: "Huyết tương Citrate", reference_range: "2.0 – 4.0", price: 80000 },

  // Miễn dịch Virus & Tuyến giáp (Immunology & Thyroid)
  { code: "HBsAg", name: "Kháng nguyên bề mặt Viêm gan B", category: "Miễn dịch", unit: "S/CO", sample_type: "Huyết thanh", reference_range: "Âm tính (< 1.0)", price: 100000 },
  { code: "Anti-HBs", name: "Kháng thể bề mặt Viêm gan B (Định lượng)", category: "Miễn dịch", unit: "mIU/mL", sample_type: "Huyết thanh", reference_range: ">= 10 (Có miễn dịch)", price: 110000 },
  { code: "Anti-HCV", name: "Kháng thể Viêm gan C (Anti-HCV)", category: "Miễn dịch", unit: "S/CO", sample_type: "Huyết thanh", reference_range: "Âm tính (< 1.0)", price: 120000 },
  { code: "TSH", name: "Hormone kích thích tuyến giáp (TSH siêu nhạy)", category: "Miễn dịch", unit: "uIU/mL", sample_type: "Huyết thanh", reference_range: "0.27 – 4.20", price: 110000 },
  { code: "FT4", name: "Thyroxine tự do (Free T4)", category: "Miễn dịch", unit: "pmol/L", sample_type: "Huyết thanh", reference_range: "12.0 – 22.0", price: 120000 },
  { code: "FT3", name: "Triiodothyronine tự do (Free T3)", category: "Miễn dịch", unit: "pmol/L", sample_type: "Huyết thanh", reference_range: "3.1 – 6.8", price: 120000 },
  { code: "Anti-TPO", name: "Kháng thể kháng peroxidase tuyến giáp (Anti-TPO)", category: "Miễn dịch", unit: "IU/mL", sample_type: "Huyết thanh", reference_range: "< 34", price: 160000 }
];

for (const c of criteriaToUpsert) {
  db.test_criteria.updateOne(
    { code: c.code },
    {
      $set: {
        code: c.code,
        name: c.name,
        category: c.category,
        unit: c.unit,
        sample_type: c.sample_type,
        reference_range: c.reference_range,
        price: c.price,
        is_active: true,
        updated_at: new Date()
      },
      $setOnInsert: {
        created_at: new Date()
      }
    },
    { upsert: true }
  );
}
print(`Upserted ${criteriaToUpsert.length} test criteria successfully.`);

// 2. MASTER CLINICAL TEST PACKAGES
const masterPackages = [
  // 1. GÓI TẦM SOÁT THIẾU MÁU & BỆNH LÝ HUYẾT SẮC TỐ (THE USER SPECIFICATION)
  {
    code: "PKG-ANEMIA-01",
    name: "Gói Xét Nghiệm Tầm Soát Thiếu Máu & Bệnh Lý Huyết Sắc Tố",
    category: "Huyết học",
    price: 1200000,
    discountPrice: 620000,
    highlights: [
      "Tìm nguyên nhân mệt mỏi, xanh xao, chóng mặt kéo dài",
      "Phân biệt đúng loại thiếu máu trong 1 lần xét nghiệm (thiếu sắt vs thalassemia vs thiếu vitamin B12/B9)",
      "Hỗ trợ điều trị đúng, tránh tự ý bổ sung sắt có thể gây ứ sắt độc hại",
      "Miễn phí lấy mẫu tận nơi & tư vấn 1-1 với Bác sĩ chuyên khoa Huyết học"
    ],
    targetAudience: "Người có dấu hiệu thiếu máu: mệt mỏi kéo dài, hoa mắt, chóng mặt, da xanh xao, khó thở khi gắng sức;\nNgười có chế độ ăn thiếu dinh dưỡng, ăn thuần chay kéo dài hoặc kém hấp thu đường tiêu hóa;\nPhụ nữ trong độ tuổi sinh sản (25 - 30 tuổi), rong kinh, chuẩn bị mang thai hoặc sau sinh;\nNgười có tiền sử gia đình mắc bệnh lý huyết học di truyền (Thalassemia, tan máu bẩm sinh);\nNgười có bệnh lý mạn tính: viêm đường ruột, suy thận mạn hoặc bệnh tự miễn.",
    description: "Gói xét nghiệm tầm soát thiếu máu là tập hợp các xét nghiệm giúp đánh giá toàn diện số lượng, chất lượng và khả năng tạo hồng cầu của cơ thể, từ đó phát hiện sớm, xác định đúng nguyên nhân và hỗ trợ xây dựng kế hoạch điều trị phù hợp, tránh bổ sung sắt hoặc vitamin không cần thiết.\n\nThiếu máu dinh dưỡng rất thường gặp và có thể được phát hiện sớm thông qua kết quả xét nghiệm cùng các dấu hiệu nghi ngờ hiện có, giúp tránh được các biến chứng thần kinh hoặc rối loạn chức năng nhận thức trong tương lai.\n\nTrong nhiều trường hợp, gói xét nghiệm sẽ giúp phân biệt nguyên nhân thiếu máu dinh dưỡng với nhiều tình trạng thiếu máu khác, chẳng hạn như thiếu máu do viêm mạn tính, rối loạn kinh nguyệt hoặc các bệnh lý mang gen di truyền (Thalassemia).",
    tat: "Sau 2 tiếng 20 phút cùng ngày (nếu nhận mẫu trước 21:30 T2-T7 hoặc trước 20:30 CN); 8:50 ngày hôm sau (nếu nhận mẫu sau mốc trên); 14:50 cùng ngày đối với gói Nâng cao (nếu nhận trước 10:00).",
    preparationNotes: "Nên lấy mẫu vào buổi sáng (tốt nhất trước 10:00) sau khi nhịn ăn 8 - 12 tiếng; chỉ được uống nước lọc.\nUống khoảng 250ml nước lọc trước khi lấy mẫu, tránh để cơ thể thiếu nước gây cô đặc máu làm sai lệch kết quả.\nThông báo với BMS MedLab về thông tin sức khỏe, tiền sử điều trị và tất cả thuốc, viên uống vi chất hoặc TPCN đang dùng; tạm ngưng uống viên sắt, vitamin ít nhất 24 giờ trước khi lấy máu (các thuốc điều trị bệnh mạn tính khác vẫn uống bình thường).\nKhông tự ý ngừng thuốc điều trị khi chưa có hướng dẫn từ bác sĩ chuyên khoa.\nThông báo với nhân viên y tế nếu bạn đang trong kỳ kinh nguyệt hoặc đã từng truyền máu trong 3 - 4 tháng gần đây.",
    sampleTube: "EDTA-Tím & Serum-Đỏ/Vàng",
    sampleVolumeMl: 4.0,
    tests: [
      { code: "CBC", name: "Tổng phân tích tế bào máu ngoại vi (Công thức máu 24 thông số)", sampleType: "[Máu toàn phần EDTA]", price: 98000, clinicalMeaning: "Đánh giá số lượng và đặc điểm hồng cầu, bạch cầu, tiểu cầu. Chỉ số Hb giúp xác định có thiếu máu và mức độ thiếu máu. Các chỉ số MCV, MCH, MCHC, RDW giúp phân loại thiếu máu hồng cầu nhỏ, bình thường hoặc to để định hướng các nguyên nhân như thiếu sắt, thalassemia hay thiếu vitamin B12, B9." },
      { code: "B12", name: "Định lượng Vitamin B12 [Máu]", sampleType: "[Huyết thanh]", price: 173000, clinicalMeaning: "Vitamin B12 là vitamin thiết yếu cho tổng hợp ADN, tạo máu và duy trì chức năng thần kinh. Xét nghiệm hỗ trợ xác định thiếu máu liên quan đến thiếu vitamin B12 khi được đánh giá cùng chỉ số công thức máu." },
      { code: "FOLATE", name: "Định lượng Vitamin B9 (Axit Folic / Folate) [Máu]", sampleType: "[Huyết thanh]", price: 190000, clinicalMeaning: "Vitamin B9 là vitamin cần thiết cho tổng hợp ADN, phân chia tế bào và tạo hồng cầu. Xét nghiệm hỗ trợ xác định thiếu máu liên quan đến thiếu vitamin B9 khi được đánh giá cùng chỉ số công thức máu." },
      { code: "FERRITIN", name: "Định lượng Ferritin [Máu]", sampleType: "[Huyết thanh]", price: 139000, clinicalMeaning: "Ferritin là protein dự trữ sắt có nồng độ trong máu và thường tương quan với lượng sắt dự trữ của cơ thể. Xét nghiệm giúp nhận diện cạn kiệt dự trữ sắt hoặc thiếu sắt từ sớm trước khi thiếu máu trở nên rõ rệt." },
      { code: "TRANSFERRIN", name: "Định lượng Transferrin [Máu]", sampleType: "[Huyết thanh]", price: 68000, clinicalMeaning: "Transferrin là protein chủ yếu do gan tổng hợp, có nhiệm vụ gắn và vận chuyển sắt trong tuần hoàn đến tủy xương. Transferrin giúp tính độ bão hòa transferrin (TSAT) qua đó phản ánh lượng sắt sẵn có cho tạo máu." },
      { code: "TIBC", name: "Khả năng gắn sắt toàn phần (TIBC) [Máu]", sampleType: "[Huyết thanh]", price: 85000, clinicalMeaning: "Đo lường năng lực gắn kết sắt tối đa của huyết thanh, phản ánh tình trạng thiếu sắt tuyệt đối hay ứ trệ sắt." },
      { code: "RETIC", name: "Đếm hồng cầu lưới [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 104000, clinicalMeaning: "Đánh giá khả năng đáp ứng tạo máu của tủy xương trước tình trạng thiếu máu." },
      { code: "HGB_ELEC", name: "Sàng lọc bệnh huyết sắc tố (Điện di Hb) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 268000, clinicalMeaning: "Phân tích các thành phần HbA, HbA2, HbF nhằm chẩn đoán xác định bệnh Thalassemia và các biến thể huyết sắc tố di truyền." }
    ]
  },

  // 2. GÓI ĐÁNH GIÁ CHỨC NĂNG GAN & HUYẾT HỌC
  {
    code: "PKG-LIVER-01",
    name: "Gói Đánh Giá Chức Năng Gan & Huyết Học Chuyên Sâu",
    category: "Sinh hóa & Huyết học",
    price: 450000,
    discountPrice: 280000,
    highlights: [
      "Đánh giá sớm mức độ hủy hoại tế bào gan qua men ALT, AST, GGT",
      "Khảo sát chức năng bài tiết mật và chuyển hóa Bilirubin",
      "Kiểm tra khả năng tổng hợp protein huyết tương của gan",
      "Phát hiện sớm thiếu máu và giảm tiểu cầu do xơ gan hoặc tăng áp lực tĩnh mạch cửa"
    ],
    targetAudience: "Người thường xuyên sử dụng rượu bia, đồ uống có cồn hoặc hút thuốc lá;\nNgười đang điều trị các loại thuốc tây dài ngày (hạ mỡ máu, giảm đau, kháng sinh, thuốc trị lao);\nNgười có các biểu hiện: mệt mỏi, chán ăn, vàng mắt, vàng da, nước tiểu sẫm màu, mẩn ngứa;\nNgười có tiền sử nhiễm virus viêm gan B, C hoặc phát hiện gan nhiễm mỡ qua siêu âm.",
    description: "Gan là nhà máy hóa chất và thải độc trung tâm của cơ thể. Gói xét nghiệm khảo sát toàn diện từ mức độ hoại tử tế bào gan (AST/ALT/GGT), chức năng bài tiết mật (Bilirubin toàn phần), khả năng tổng hợp protein (Albumin) cho đến các chỉ số tế bào máu ngoại vi phản ánh tình trạng cường lách và giảm tiểu cầu thường gặp ở bệnh gan mạn tính.",
    tat: "Sau 60 – 90 phút có kết quả kể từ thời điểm nhận mẫu đạt chuẩn ISO 15189.",
    preparationNotes: "Nhịn ăn ít nhất 8 - 12 tiếng trước khi lấy máu (chỉ uống nước lọc).\nTuyệt đối không uống rượu, bia, đồ uống có cồn trong vòng 48 tiếng trước khi làm xét nghiệm.\nThông báo cho bác sĩ danh mục các loại thuốc đang sử dụng hàng ngày.",
    sampleTube: "Serum-Đỏ/Vàng & EDTA-Tím",
    sampleVolumeMl: 4.0,
    tests: [
      { code: "ALT", name: "Men gan ALT (GPT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Men đặc hiệu cao của tế bào gan, tăng cao cảnh báo hoại tử tế bào gan cấp tính hoặc mạn tính." },
      { code: "AST", name: "Men gan AST (GOT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Men nội bào gan và tim; tỷ số AST/ALT > 2 gợi ý tổn thương tế bào gan do rượu bia." },
      { code: "GGT", name: "Men gan GGT (Gamma GT) [Máu]", sampleType: "[Huyết thanh]", price: 65000, clinicalMeaning: "Chỉ số rất nhạy cảm phát hiện ứ mật, tổn thương đường mật và nhiễm độc rượu bia." },
      { code: "BILI_TOT", name: "Bilirubin toàn phần [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Đánh giá chức năng lọc và đào thải sắc tố mật của gan, tăng gây vàng da vàng mắt." },
      { code: "ALB", name: "Albumin huyết thanh [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Đánh giá chức năng tổng hợp protein của gan; giảm phản ánh suy chức năng gan mạn tính." },
      { code: "CBC", name: "Tổng phân tích tế bào máu ngoại vi (24 thông số) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 98000, clinicalMeaning: "Đánh giá bạch cầu, hồng cầu và đặc biệt là tiểu cầu (tiểu cầu giảm thường đi kèm xơ gan cường lách)." }
    ]
  },

  // 3. GÓI TẦM SOÁT ĐÁI THÁO ĐƯỜNG & RỐI LOẠN CHUYỂN HÓA
  {
    code: "PKG-DIAB-01",
    name: "Gói Tầm Soát Đái Tháo Đường & Rối Loạn Chuyển Hóa Chuyên Sâu",
    category: "Sinh hóa",
    price: 500000,
    discountPrice: 380000,
    highlights: [
      "Phát hiện tiền đái tháo đường từ sớm trước khi xuất hiện triệu chứng lâm sàng",
      "Đo HbA1c phản ánh mức đường huyết trung bình trong suốt 3 tháng gần nhất",
      "Đánh giá biến chứng sớm lên chức năng lọc cầu thận (Microalbumin niệu/Creatinine)",
      "Tư vấn chế độ dinh dưỡng và kiểm soát chỉ số Glycemic cá thể hóa"
    ],
    targetAudience: "Người từ 35 tuổi trở lên hoặc có tiền sử gia đình (bố, mẹ, anh chị em ruột) mắc đái tháo đường;\nNgười thừa cân, béo phì (BMI > 23), ít vận động thể lực, có vòng eo lớn;\nPhụ nữ có tiền sử đái tháo đường thai kỳ hoặc sinh con nặng trên 4kg;\nNgười có các triệu chứng: ăn nhiều, uống nhiều, tiểu nhiều, sụt cân không rõ nguyên nhân, hay khát nước ban đêm;\nBệnh nhân đã chẩn đoán tiểu đường cần theo dõi định kỳ hiệu quả điều trị.",
    description: "Đái tháo đường là bệnh lý chuyển hóa tiến triển âm thầm, làm tổn thương mạch máu lớn và vi mạch, dẫn đến biến chứng mù lòa, suy thận, đột quỵ và hoại tử chi. Gói xét nghiệm kết hợp Glucose đói cùng HbA1c (tiêu chuẩn vàng ADA) và đánh giá chức năng thận, mang lại giải pháp tầm soát và quản lý đường huyết toàn diện.",
    tat: "Sau 60 – 90 phút có kết quả kể từ thời điểm nhận mẫu.",
    preparationNotes: "Nhịn ăn tuyệt đối từ 8 đến 10 tiếng trước khi lấy máu (lấy máu từ 7:00 - 9:00 sáng). Chỉ được uống một ít nước lọc tinh khiết.\nKhông uống cà phê, trà đặc, nước ngọt hoặc hút thuốc lá vào buổi sáng trước xét nghiệm.\nNgười đang dùng thuốc hạ đường huyết hoặc tiêm Insulin: TẠM NGƯNG dùng thuốc vào sáng lấy máu, mang theo thuốc đến phòng khám để uống/tiêm ngay sau khi hoàn tất lấy mẫu máu.",
    sampleTube: "Fluoride-Xám (Glucose) & EDTA-Tím (HbA1c)",
    sampleVolumeMl: 3.5,
    tests: [
      { code: "GLU", name: "Đường huyết đói (Glucose) [Máu]", sampleType: "[Huyết tương NaF]", price: 40000, clinicalMeaning: "Định lượng nồng độ glucose tự do trong máu sau nhịn ăn 8-10h, tiêu chuẩn đầu tay phát hiện đái tháo đường." },
      { code: "HBA1C", name: "Hemoglobin A1c (HbA1c HPLC) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 180000, clinicalMeaning: "Tỷ lệ phân tử hemoglobin gắn đường, phản ánh bức tranh kiểm soát đường huyết ổn định trong 90 ngày vừa qua." },
      { code: "INSULIN", name: "Định lượng Insulin máu lúc đói [Máu]", sampleType: "[Huyết thanh]", price: 150000, clinicalMeaning: "Đánh giá tình trạng kháng insulin và chức năng tiết của tế bào beta đảo tụy." },
      { code: "CRE", name: "Creatinine máu & eGFR [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Tầm soát sớm biến chứng suy giảm chức năng lọc cầu thận do đái tháo đường." }
    ]
  },

  // 4. GÓI XÉT NGHIỆM TỔNG QUÁT CƠ BẢN
  {
    code: "PKG-GEN-01",
    name: "Gói Xét Nghiệm Sức Khỏe Tổng Quát Định Kỳ Toàn Diện",
    category: "Tổng quát",
    price: 750000,
    discountPrice: 550000,
    highlights: [
      "Khảo sát toàn diện 5 cơ quan trọng yếu: Máu, Gan, Thận, Đường huyết, Mỡ máu",
      "Phát hiện sớm các bệnh lý mạn tính không lây phổ biến hàng đầu tại Việt Nam",
      "Quy trình xét nghiệm tự động hóa khép kín chuẩn ISO 15189:2022",
      "Bác sĩ chuyên khoa giải thích cặn kẽ từng chỉ số và lập hồ sơ sức khỏe điện tử"
    ],
    targetAudience: "Mọi người trưởng thành từ 18 tuổi trở lên cần kiểm tra sức khỏe định kỳ mỗi 6 - 12 tháng;\nNhân viên văn phòng, người làm việc trong môi trường căng thẳng, ít vận động;\nNgười muốn tầm soát sức khỏe tiền hôn nhân hoặc kiểm tra sức khỏe tổng thể chủ động.",
    description: "Khám sức khỏe định kỳ là lá chắn phòng bệnh chủ động hiệu quả nhất. Gói xét nghiệm tổng quát được thiết kế khoa học bao gồm các xét nghiệm nền tảng: Công thức máu (phát hiện thiếu máu, nhiễm trùng), Đường huyết đói (tiểu đường), Men gan & Protein (chức năng gan mật), Ure & Creatinine (chức năng bài tiết thận), Mỡ máu toàn phần (nguy cơ tim mạch) và Acid Uric (bệnh gút).",
    tat: "Sau 90 – 120 phút có kết quả đầy đủ.",
    preparationNotes: "Nhịn ăn sáng từ 8 - 12 tiếng trước khi lấy máu (uống nước lọc bình thường).\nKhông uống rượu bia, nước tăng lực hoặc tập thể dục gắng sức trong vòng 24 giờ trước khi khám.",
    sampleTube: "EDTA-Tím, Serum-Đỏ/Vàng",
    sampleVolumeMl: 4.0,
    tests: [
      { code: "CBC", name: "Tổng phân tích tế bào máu ngoại vi (24 thông số Laser) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 98000, clinicalMeaning: "Kiểm tra tình trạng hồng cầu, bạch cầu, tiểu cầu, tầm soát thiếu máu và nhiễm khuẩn." },
      { code: "GLU", name: "Glucose máu lúc đói [Máu]", sampleType: "[Huyết thanh]", price: 40000, clinicalMeaning: "Sàng lọc nguy cơ đái tháo đường và hạ đường huyết." },
      { code: "ALT", name: "Men gan ALT (GPT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Kiểm tra tổn thương nhu mô gan do độc chất, rượu bia hoặc virus." },
      { code: "AST", name: "Men gan AST (GOT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Đánh giá tổn thương tế bào gan và tế bào cơ tim." },
      { code: "URE", name: "Ure huyết thanh [Máu]", sampleType: "[Huyết thanh]", price: 40000, clinicalMeaning: "Đánh giá chuyển hóa đạm và khả năng bài tiết của thận." },
      { code: "CRE", name: "Creatinine máu & eGFR [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Đánh giá mức độ lọc cầu thận và chức năng bài tiết thận." },
      { code: "CHO", name: "Cholesterol toàn phần [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Tầm soát mỡ máu và nguy cơ xơ vữa tim mạch." },
      { code: "TRI", name: "Triglyceride máu [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Đánh giá nguy cơ rối loạn mỡ máu và viêm tụy cấp." },
      { code: "UA", name: "Acid Uric huyết thanh [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Tầm soát bệnh Gút (Gout) và tăng acid uric máu." }
    ]
  },

  // 5. TỔNG PHÂN TÍCH TẾ BÀO MÁU NGOẠI VI
  {
    code: "CBC-01",
    name: "Tổng Phân Tích Tế Bào Máu Ngoại Vi (24 Thông Số Laser)",
    category: "Huyết học",
    price: 180000,
    discountPrice: 98000,
    highlights: [
      "Phân tích laser 5 thành phần bạch cầu với độ chính xác cao nhất",
      "Đo lường 24 chỉ số tế bào máu: Hồng cầu, Huyết sắc tố, Bạch cầu, Tiểu cầu",
      "Phát hiện sớm tình trạng thiếu máu, nhiễm trùng cấp/mạn tính và bệnh lý ác tính dòng máu",
      "Thời gian trả kết quả thần tốc chỉ 30 – 45 phút"
    ],
    targetAudience: "Người có biểu hiện mệt mỏi, sốt không rõ nguyên nhân, nổi hạch, chảy máu cam, bầm tím dưới da;\nKhám sức khỏe tổng quát hoặc kiểm tra tiền phẫu thuật, thủ thuật;\nNgười cần theo dõi số lượng bạch cầu, tiểu cầu trong sốt xuất huyết Dengue hoặc nhiễm virus.",
    description: "Xét nghiệm công thức máu toàn phần (CBC) là xét nghiệm huyết học cơ bản và quan trọng bậc nhất, cung cấp bức tranh tổng thể về tình trạng các tế bào máu: hồng cầu vận chuyển oxy, bạch cầu bảo vệ miễn dịch và tiểu cầu tham gia đông cầm máu.",
    tat: "Sau 30 – 45 phút kể từ khi nhận mẫu.",
    preparationNotes: "Không cần nhịn ăn trước khi lấy máu. Nên uống một ly nước lọc để lấy ven dễ dàng hơn.",
    sampleTube: "EDTA-Tím",
    sampleVolumeMl: 2.0,
    tests: [
      { code: "RBC", name: "Số lượng hồng cầu (RBC) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 30000, clinicalMeaning: "Đánh giá số lượng hồng cầu lưu hành trong tuần hoàn máu." },
      { code: "HGB", name: "Huyết sắc tố (Hemoglobin) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 30000, clinicalMeaning: "Chỉ số quyết định để chẩn đoán mức độ thiếu máu." },
      { code: "HCT", name: "Dung tích hồng cầu (Hematocrit) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 25000, clinicalMeaning: "Tỷ lệ thể tích hồng cầu chiếm trong máu toàn phần, tăng trong cô đặc máu sốt xuất huyết." },
      { code: "MCV", name: "Thể tích trung bình hồng cầu (MCV) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 25000, clinicalMeaning: "Phân biệt thiếu máu hồng cầu nhỏ, bình thường hay to." },
      { code: "WBC", name: "Số lượng bạch cầu (WBC) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 30000, clinicalMeaning: "Tăng trong nhiễm trùng vi khuẩn hoặc bệnh lý tăng sinh tủy; giảm trong nhiễm virus hoặc suy tủy." },
      { code: "PLT", name: "Số lượng tiểu cầu (PLT) [Máu]", sampleType: "[Máu toàn phần EDTA]", price: 30000, clinicalMeaning: "Đánh giá nguy cơ xuất huyết (khi tiểu cầu giảm) hoặc huyết khối (khi tiểu cầu tăng)." }
    ]
  },

  // 6. SINH HÓA MÁU CƠ BẢN
  {
    code: "BIOCHEM-01",
    name: "Sinh Hóa Máu Cơ Bản (Glucose, AST/ALT, Ure, Creatinine)",
    category: "Sinh hóa",
    price: 250000,
    discountPrice: 200000,
    highlights: [
      "Kiểm tra các chức năng chuyển hóa nền tảng của cơ thể",
      "Đánh giá mức độ tổn thương tế bào gan và khả năng thanh thải của thận",
      "Sàng lọc nguy cơ đái tháo đường sớm",
      "Quy chuẩn xét nghiệm trên hệ thống sinh hóa tự động AU680 chuẩn hóa quốc tế"
    ],
    targetAudience: "Người cần kiểm tra sức khỏe tổng quát, theo dõi định kỳ chức năng gan thận;\nNgười chuẩn bị dùng các thuốc có chuyển hóa qua gan hoặc bài tiết qua thận.",
    description: "Gói sinh hóa máu cơ bản tập trung khảo sát 4 cơ quan cốt lõi: Tuyến tụy nội tiết (Glucose), Gan mật (AST, ALT) và Thận tiết niệu (Ure, Creatinine), cung cấp các số liệu lâm sàng thiết yếu cho chẩn đoán và điều trị bệnh nội khoa.",
    tat: "Sau 60 – 90 phút có kết quả.",
    preparationNotes: "Nhịn ăn sáng từ 8 - 12 tiếng (chỉ uống nước lọc) để đảm bảo độ chính xác của chỉ số đường huyết.",
    sampleTube: "Serum-Đỏ/Vàng",
    sampleVolumeMl: 3.5,
    tests: [
      { code: "GLU", name: "Đường huyết đói (Glucose) [Máu]", sampleType: "[Huyết thanh]", price: 40000, clinicalMeaning: "Đánh giá nồng độ đường trong máu lúc đói." },
      { code: "AST", name: "Men gan AST (GOT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Khảo sát hoại tử tế bào gan và cơ tim." },
      { code: "ALT", name: "Men gan ALT (GPT) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Đánh giá đặc hiệu tổn thương tế bào gan." },
      { code: "URE", name: "Ure máu [Máu]", sampleType: "[Huyết thanh]", price: 40000, clinicalMeaning: "Đánh giá chuyển hóa đạm và chức năng lọc của thận." },
      { code: "CRE", name: "Creatinine máu & eGFR [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Chỉ số vàng đánh giá mức lọc cầu thận." }
    ]
  },

  // 7. ĐÔNG MÁU CƠ BẢN
  {
    code: "COAG-01",
    name: "Đông Máu Cơ Bản (PT, APTT, Fibrinogen, INR)",
    category: "Đông máu",
    price: 280000,
    discountPrice: 220000,
    highlights: [
      "Đánh giá toàn diện 2 con đường đông máu ngoại sinh và nội sinh",
      "Bắt buộc trước mọi can thiệp phẫu thuật, thủ thuật xâm lấn, nhổ răng",
      "Theo dõi liều lượng thuốc chống đông máu kháng Vitamin K qua chỉ số INR",
      "Phát hiện sớm các rối loạn đông máu di truyền hoặc mắc phải"
    ],
    targetAudience: "Bệnh nhân chuẩn bị can thiệp ngoại khoa, phẫu thuật, nhổ răng, thủ thuật xâm lấn;\nNgười đang điều trị thuốc chống đông máu (Warfarin, Sintrom);\nNgười có biểu hiện xuất huyết dưới da tự nhiên, chảy máu chân răng khó cầm, vết bầm tím lan rộng.",
    description: "Bộ xét nghiệm đông máu cơ bản đánh giá khả năng tạo cục máu đông sinh lý của cơ thể qua 2 con đường ngoại sinh (PT) và nội sinh (APTT), cùng nồng độ yếu tố đông máu nền tảng Fibrinogen, đảm bảo an toàn tuyệt đối cho người bệnh trước phẫu thuật.",
    tat: "Sau 45 – 60 phút có kết quả.",
    preparationNotes: "Không cần nhịn ăn. Cần thông báo chính xác cho nhân viên y tế loại thuốc chống đông đang sử dụng và giờ uống thuốc gần nhất.",
    sampleTube: "Citrate-XanhLam (tỷ lệ 1:9 chuẩn xác)",
    sampleVolumeMl: 2.5,
    tests: [
      { code: "PT_SEC", name: "Thời gian Prothrombin (PT - giây và tỷ lệ %) [Máu]", sampleType: "[Huyết tương Citrate]", price: 70000, clinicalMeaning: "Khảo sát con đường đông máu ngoại sinh." },
      { code: "PT_INR", name: "Chỉ số bình chuẩn quốc tế INR [Máu]", sampleType: "[Huyết tương Citrate]", price: 60000, clinicalMeaning: "Theo dõi hiệu quả và an toàn của thuốc chống đông kháng vitamin K." },
      { code: "APTT", name: "Thời gian Thromboplastin một phần hoạt hóa (APTT) [Máu]", sampleType: "[Huyết tương Citrate]", price: 70000, clinicalMeaning: "Khảo sát con đường đông máu nội sinh, tầm soát bệnh Hemophilia." },
      { code: "FIBRIN", name: "Định lượng Fibrinogen [Máu]", sampleType: "[Huyết tương Citrate]", price: 80000, clinicalMeaning: "Yếu tố I đông máu, cần thiết cho sự hình thành cục máu đông vững chắc." }
    ]
  },

  // 8. TẦM SOÁT VIÊM GAN B & C
  {
    code: "HEPA-01",
    name: "Tầm Soát Viêm Gan Siêu Vi B & C (HBsAg, Anti-HBs, Anti-HCV)",
    category: "Miễn dịch",
    price: 400000,
    discountPrice: 320000,
    highlights: [
      "Phát hiện chính xác tình trạng nhiễm virus viêm gan B và C mạn tính",
      "Định lượng kháng thể Anti-HBs kiểm tra mức độ miễn dịch sau tiêm phòng vaccine",
      "Hệ thống miễn dịch hóa phát quang tự động thế hệ mới độ nhạy 99.9%",
      "Phát hiện sớm để điều trị kịp thời, ngăn ngừa xơ gan và ung thư gan nguyên phát (HCC)"
    ],
    targetAudience: "Người chưa từng xét nghiệm viêm gan B, C hoặc sống chung với người nhiễm virus viêm gan;\nNgười chuẩn bị tiêm vaccine viêm gan B cần kiểm tra kháng thể bảo vệ;\nNgười có tiền sử truyền máu, xăm mình, quan hệ tình dục không an toàn hoặc dùng chung dụng cụ y tế.",
    description: "Virus viêm gan B và C là nguyên nhân hàng đầu dẫn đến xơ gan và ung thư biểu mô tế bào gan tại Việt Nam. Gói tầm soát giúp xác định chính xác cơ thể có đang nhiễm virus hay không (HBsAg, Anti-HCV) và nồng độ kháng thể bảo vệ cơ thể (Anti-HBs) để có kế hoạch tiêm nhắc lại vaccine kịp thời.",
    tat: "Sau 60 – 90 phút có kết quả.",
    preparationNotes: "Không bắt buộc nhịn ăn. Uống đủ nước lọc trước khi lấy mẫu.",
    sampleTube: "Serum-Đỏ/Vàng",
    sampleVolumeMl: 4.0,
    tests: [
      { code: "HBsAg", name: "Kháng nguyên bề mặt Viêm gan B (HBsAg) [Máu]", sampleType: "[Huyết thanh]", price: 100000, clinicalMeaning: "Dấu ấn xác định cơ thể có đang nhiễm virus viêm gan B hay không." },
      { code: "Anti-HBs", name: "Kháng thể bề mặt Viêm gan B (Anti-HBs định lượng) [Máu]", sampleType: "[Huyết thanh]", price: 110000, clinicalMeaning: "Đo nồng độ kháng thể bảo vệ, nếu < 10 mIU/mL cần tiêm nhắc lại vaccine." },
      { code: "Anti-HCV", name: "Kháng thể Viêm gan C (Anti-HCV) [Máu]", sampleType: "[Huyết thanh]", price: 120000, clinicalMeaning: "Sàng lọc phơi nhiễm với virus viêm gan C mạn tính." }
    ]
  },

  // 9. BỘ MỠ MÁU TOÀN PHẦN & NGUY CƠ TIM MẠCH
  {
    code: "LIPID-01",
    name: "Bộ Mỡ Máu Toàn Phần & Đánh Giá Nguy Cơ Tim Mạch",
    category: "Sinh hóa",
    price: 260000,
    discountPrice: 200000,
    highlights: [
      "Khảo sát đầy đủ 4 phân đoạn lipid máu chuẩn khuyến cáo Hội Tim Mạch Việt Nam",
      "Đo LDL-C (mỡ máu xấu) và HDL-C (mỡ máu tốt) trực tiếp",
      "Tính toán tỷ số xơ vữa động mạch và nguy cơ nhồi máu cơ tim, đột quỵ não",
      "Theo dõi hiệu quả điều trị thuốc hạ lipid máu nhóm Statin"
    ],
    targetAudience: "Người từ 30 tuổi trở lên hoặc người có huyết áp cao, hút thuốc lá, đái tháo đường;\nNgười có người thân mắc bệnh mạch vành, tai biến mạch máu não sớm trước 55 tuổi;\nNgười thừa cân, béo phì, chế độ ăn nhiều dầu mỡ động vật hoặc ít vận động.",
    description: "Rối loạn chuyển hóa lipid máu là nguyên nhân trực tiếp hình thành các mảng xơ vữa gây hẹp tắc mạch vành tim và mạch máu não. Gói xét nghiệm định lượng trực tiếp 4 thông số: Cholesterol toàn phần, Triglyceride, HDL-C và LDL-C, giúp bác sĩ phân tầng nguy cơ tim mạch và xây dựng chiến lược phòng ngừa đột quỵ hiệu quả.",
    tat: "Sau 60 – 90 phút có kết quả.",
    preparationNotes: "BẮT BUỘC NHỊN ĂN TỪ 10 ĐẾN 12 TIẾNG trước khi lấy máu (chỉ uống nước lọc). Tuyệt đối không ăn đồ dầu mỡ, không uống rượu bia trong vòng 24 - 48 giờ trước xét nghiệm vì sẽ làm sai lệch nghiêm trọng chỉ số Triglyceride.",
    sampleTube: "Serum-Đỏ/Vàng",
    sampleVolumeMl: 3.0,
    tests: [
      { code: "CHO", name: "Cholesterol toàn phần (Total Cholesterol) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Tổng lượng cholesterol trong máu, mục tiêu khuyến nghị < 5.2 mmol/L." },
      { code: "TRI", name: "Triglyceride (Chất béo trung tính) [Máu]", sampleType: "[Huyết thanh]", price: 45000, clinicalMeaning: "Dạng chất béo dự trữ, tăng cao gây nguy cơ xơ vữa và viêm tụy cấp (> 10 mmol/L)." },
      { code: "HDL", name: "HDL-Cholesterol (Mỡ tốt) [Máu]", sampleType: "[Huyết thanh]", price: 55000, clinicalMeaning: "Vận chuyển cholesterol dư thừa từ mạch máu về gan, càng cao càng bảo vệ tim mạch." },
      { code: "LDL", name: "LDL-Cholesterol trực tiếp (Mỡ xấu) [Máu]", sampleType: "[Huyết thanh]", price: 55000, clinicalMeaning: "Thủ phạm chính lắng đọng tạo mảng xơ vữa gây hẹp tắc lòng mạch vành và mạch não." }
    ]
  },

  // 10. GÓI TẦM SOÁT BỆNH LÝ TUYẾN GIÁP
  {
    code: "PKG-THYROID-01",
    name: "Gói Tầm Soát Rối Loạn Chức Năng Tuyến Giáp Chuyên Sâu",
    category: "Miễn dịch",
    price: 480000,
    discountPrice: 380000,
    highlights: [
      "Đánh giá chính xác tình trạng Cường giáp (Basedow) hoặc Suy giáp tiềm ẩn",
      "Bộ 3 hormone cốt lõi TSH, FT3, FT4 theo khuyến cáo Hiệp Hội Tuyến Giáp Hoa Kỳ (ATA)",
      "Tìm nguyên nhân rụng tóc, tăng/giảm cân bất thường, hồi hộp đánh trống ngực",
      "Miễn phí tư vấn chuyên khoa Nội tiết"
    ],
    targetAudience: "Người có các triệu chứng nghi ngờ: sụt cân nhanh dù ăn nhiều, run tay, nhịp tim nhanh, hồi hộp, sợ nóng, đổ mồ hôi nhiều;\nNgười có triệu chứng suy giáp: mệt mỏi, sợ lạnh, tăng cân không kiểm soát, táo bón kéo dài, rụng tóc, da khô, giảm trí nhớ;\nPhụ nữ rối loạn kinh nguyệt, hiếm muộn hoặc phụ nữ đang lên kế hoạch mang thai;\nNgười phát hiện bướu cổ hoặc nhân tuyến giáp qua siêu âm.",
    description: "Tuyến giáp điều hòa tốc độ chuyển hóa của toàn bộ cơ thể. Rối loạn hormone tuyến giáp có thể gây suy nhược, rối loạn nhịp tim và vô sinh. Gói xét nghiệm định lượng bộ ba TSH, FT4, FT3 và kháng thể Anti-TPO giúp chẩn đoán sớm và chính xác các bệnh lý cường giáp, suy giáp và viêm giáp tự miễn.",
    tat: "Sau 90 – 120 phút có kết quả.",
    preparationNotes: "Nên lấy máu vào buổi sáng. Nhịn ăn 6-8 tiếng. Tạm ngưng uống viên vi chất có chứa hàm lượng Biotin (Vitamin B7/H) cao ít nhất 48 tiếng trước khi xét nghiệm vì Biotin gây nhiễu phương pháp hóa phát quang miễn dịch.",
    sampleTube: "Serum-Đỏ/Vàng",
    sampleVolumeMl: 3.5,
    tests: [
      { code: "TSH", name: "Hormone kích thích tuyến giáp (TSH siêu nhạy) [Máu]", sampleType: "[Huyết thanh]", price: 110000, clinicalMeaning: "Chỉ số nhạy nhất đánh giá chức năng tuyến yên và phát hiện sớm rối loạn tuyến giáp." },
      { code: "FT4", name: "Thyroxine tự do (Free T4) [Máu]", sampleType: "[Huyết thanh]", price: 120000, clinicalMeaning: "Dạng hoạt tính sinh học chính của hormone giáp trong tuần hoàn." },
      { code: "FT3", name: "Triiodothyronine tự do (Free T3) [Máu]", sampleType: "[Huyết thanh]", price: 120000, clinicalMeaning: "Đặc biệt hữu ích trong chẩn đoán nhiễm độc giáp T3." },
      { code: "Anti-TPO", name: "Kháng thể kháng peroxidase tuyến giáp (Anti-TPO) [Máu]", sampleType: "[Huyết thanh]", price: 160000, clinicalMeaning: "Chẩn đoán viêm tuyến giáp tự miễn Hashimoto hoặc bệnh Basedow." }
    ]
  },

  // 11. GÓI ĐÁNH GIÁ CHỨC NĂNG THẬN & GÚT
  {
    code: "PKG-RENAL-01",
    name: "Gói Đánh Giá Chức Năng Thận & Tầm Soát Bệnh Gút",
    category: "Sinh hóa",
    price: 320000,
    discountPrice: 240000,
    highlights: [
      "Đánh giá khả năng lọc và đào thải của 2 quả thận",
      "Tính toán chính xác mức lọc cầu thận ước tính (eGFR) theo công thức CKD-EPI",
      "Đo nồng độ Acid Uric huyết thanh phát hiện sớm nguy cơ lắng đọng tinh thể urat tại khớp",
      "Tổng phân tích nước tiểu 10 thông số tầm soát viêm cầu thận, nhiễm trùng tiểu"
    ],
    targetAudience: "Người có triệu chứng đau buốt khớp ngón chân cái, sưng nóng đỏ các khớp sau khi ăn nhiều đạm hoặc uống rượu bia;\nNgười có các dấu hiệu: phù mặt, phù chân, tiểu đêm nhiều lần, nước tiểu có bọt lâu tan hoặc đổi màu;\nBệnh nhân cao huyết áp, đái tháo đường cần tầm soát biến chứng suy thận định kỳ.",
    description: "Thận đảm nhận chức năng đào thải độc tố nitơ, duy trì cân bằng nội môi và điều hòa huyết áp. Gói xét nghiệm khảo sát toàn diện khả năng bài tiết qua Ure, Creatinine, điện giải đồ và Acid Uric máu, kết hợp tổng phân tích nước tiểu giúp phát hiện tổn thương cầu thận từ giai đoạn sớm.",
    tat: "Sau 60 – 90 phút có kết quả.",
    preparationNotes: "Nhịn ăn sáng từ 8 - 10 tiếng (uống đủ nước lọc). Không ăn quá nhiều thịt đỏ, hải sản hoặc nội tạng động vật trong vòng 48 tiếng trước ngày xét nghiệm.",
    sampleTube: "Serum-Đỏ/Vàng & Lọ nước tiểu vô trùng",
    sampleVolumeMl: 3.5,
    tests: [
      { code: "URE", name: "Ure huyết thanh [Máu]", sampleType: "[Huyết thanh]", price: 40000, clinicalMeaning: "Đánh giá đào thải chất độc nitơ qua thận." },
      { code: "CRE", name: "Creatinine máu & Độ lọc cầu thận ước tính (eGFR) [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Tiêu chuẩn vàng phân độ giai đoạn suy thận mạn tính." },
      { code: "UA", name: "Acid Uric huyết thanh [Máu]", sampleType: "[Huyết thanh]", price: 50000, clinicalMeaning: "Chẩn đoán xác định tăng acid uric máu và bệnh Gút." },
      { code: "ELEC", name: "Điện giải đồ (Na, K, Cl) [Máu]", sampleType: "[Huyết thanh]", price: 75000, clinicalMeaning: "Đánh giá thăng bằng nước và điện giải liên quan chức năng bài tiết của ống thận." },
      { code: "URINE_10", name: "Tổng phân tích nước tiểu 10 thông số [Nước tiểu]", sampleType: "[Nước tiểu]", price: 55000, clinicalMeaning: "Phát hiện protein niệu, hồng cầu niệu, trụ niệu phản ánh tổn thương màng lọc cầu thận." }
    ]
  }
];

// Seed to both service_packages (Admin / Booking) and test_packages (Catalog)
for (const p of masterPackages) {
  // 1. service_packages
  const spTests = p.tests.map(t => ({
    Code: t.code,
    Name: t.name,
    SampleType: t.sampleType,
    Price: t.price,
    ClinicalMeaning: t.clinicalMeaning
  }));

  db.service_packages.updateOne(
    { Code: p.code },
    {
      $set: {
        Code: p.code,
        Name: p.name,
        Category: p.category,
        Price: NumberDecimal(p.price.toString()),
        DiscountPrice: p.discountPrice ? NumberDecimal(p.discountPrice.toString()) : null,
        Description: p.description,
        PreparationNotes: p.preparationNotes,
        TargetAudience: p.targetAudience,
        Tat: p.tat,
        SampleTube: p.sampleTube,
        SampleVolumeMl: p.sampleVolumeMl,
        Highlights: p.highlights,
        Tests: spTests,
        IsActive: true,
        IsDeleted: false,
        UpdatedAt: new Date(),
        UpdatedBy: "SYSTEM_SEEDED"
      },
      $setOnInsert: {
        CreatedAt: new Date(),
        CreatedBy: "SYSTEM_SEEDED"
      }
    },
    { upsert: true }
  );

  // 2. test_packages
  db.test_packages.updateOne(
    { package_code: p.code },
    {
      $set: {
        package_code: p.code,
        package_name: p.name,
        category: p.category,
        price: p.discountPrice || p.price,
        description: p.description,
        sample_tube: p.sampleTube,
        sample_volume_ml: p.sampleVolumeMl,
        tat: p.tat,
        fasting_rule: p.preparationNotes,
        target_audience: p.targetAudience,
        is_active: true
      }
    },
    { upsert: true }
  );

  // 3. Map criteria in test_package_criteria
  for (const t of p.tests) {
    const crit = db.test_criteria.findOne({ code: t.code });
    if (crit) {
      db.test_package_criteria.updateOne(
        { package_id: p.code, criteria_id: crit.code },
        {
          $set: {
            package_id: p.code,
            criteria_id: crit.code,
            criterion_id: crit._id ? crit._id.toString() : crit.code
          }
        },
        { upsert: true }
      );
    }
  }
}

print(`Successfully seeded ${masterPackages.length} rich clinical packages to service_packages and test_packages.`);
print("=== SEEDING COMPLETED ===");
