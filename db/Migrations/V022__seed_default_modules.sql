-- V022: Seed default module_groups and modules for HDOS v3.0.
-- 5 groups, 21 modules matching the M01–M21 architecture.
-- All modules start as is_active=true. required_roles=NULL means all authenticated users.
-- Admin-only modules set required_roles = ARRAY['admin'].

-- ── Groups ───────────────────────────────────────────────────────────────────

INSERT INTO module_groups (id, slug, label, icon, sort_order) VALUES
    ('00000000-0000-0000-0001-000000000001', 'executive',    'Điều hành',          'BarChart2',    10),
    ('00000000-0000-0000-0001-000000000002', 'clinical',     'Lâm sàng',           'Stethoscope',  20),
    ('00000000-0000-0000-0001-000000000003', 'diagnostic',   'Cận lâm sàng',       'FlaskConical', 30),
    ('00000000-0000-0000-0001-000000000004', 'operations',   'Quản lý bệnh viện',  'Building2',    40),
    ('00000000-0000-0000-0001-000000000005', 'ai-analytics', 'AI & Phân tích',     'BrainCircuit', 50)
ON CONFLICT (slug) DO NOTHING;

-- ── Group 1: Điều hành ────────────────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000001',
     '00000000-0000-0000-0001-000000000001',
     'executive-dashboard', 'Dashboard Điều hành', 'LayoutDashboard',
     'KPI tổng quan toàn bệnh viện: doanh thu, giường, nhân sự, cảnh báo.',
     NULL, 10),

    ('00000000-0000-0000-0002-000000000002',
     '00000000-0000-0000-0001-000000000001',
     'operations-center', 'Trung tâm Điều phối', 'MonitorPlay',
     'Theo dõi hoạt động real-time: ambulance, OR, ER, ICU.',
     NULL, 20),

    ('00000000-0000-0000-0002-000000000003',
     '00000000-0000-0000-0001-000000000001',
     'digital-twin', 'Digital Twin Bệnh viện', 'Network',
     'Mô phỏng số bệnh viện: mô hình 3D tầng, luồng bệnh nhân, dự báo.',
     ARRAY['admin', 'doctor'], 30);

-- ── Group 2: Lâm sàng ────────────────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000004',
     '00000000-0000-0000-0001-000000000002',
     'emergency', 'Cấp cứu', 'Siren',
     'Theo dõi ER: bệnh nhân chờ, phân loại, xe cấp cứu đang đến, cảnh báo.',
     ARRAY['doctor', 'nurse', 'admin'], 10),

    ('00000000-0000-0000-0002-000000000005',
     '00000000-0000-0000-0001-000000000002',
     'inpatient', 'Nội trú', 'BedDouble',
     'Quản lý bệnh nhân nội trú: giường, NEWS2, pathway điều trị.',
     ARRAY['doctor', 'nurse', 'admin'], 20),

    ('00000000-0000-0000-0002-000000000006',
     '00000000-0000-0000-0001-000000000002',
     'outpatient', 'Ngoại trú', 'UserCheck',
     'Lịch khám, danh sách chờ, kết quả xét nghiệm ngoại trú.',
     ARRAY['doctor', 'nurse', 'admin'], 30),

    ('00000000-0000-0000-0002-000000000007',
     '00000000-0000-0000-0001-000000000002',
     'icu', 'ICU / CCU', 'HeartPulse',
     'Theo dõi chuyên sâu ICU: NEWS2, vital signs, cảnh báo L1/L2/L3.',
     ARRAY['doctor', 'nurse', 'admin'], 40),

    ('00000000-0000-0000-0002-000000000008',
     '00000000-0000-0000-0001-000000000002',
     'surgery', 'Phẫu thuật', 'Scissors',
     'Lịch mổ, trạng thái phòng mổ, thời gian phẫu thuật.',
     ARRAY['doctor', 'nurse', 'admin'], 50),

    ('00000000-0000-0000-0002-000000000009',
     '00000000-0000-0000-0001-000000000002',
     'clinical-pathway', 'Clinical Pathway', 'GitBranch',
     'Quản lý pathway điều trị chuẩn hoá theo DRG / ICD-10.',
     ARRAY['doctor', 'admin'], 60);

-- ── Group 3: Cận lâm sàng ────────────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000010',
     '00000000-0000-0000-0001-000000000003',
     'laboratory', 'Xét nghiệm', 'FlaskConical',
     'Kết quả xét nghiệm, TAT, giá trị bất thường, tải công việc lab.',
     ARRAY['doctor', 'nurse', 'admin'], 10),

    ('00000000-0000-0000-0002-000000000011',
     '00000000-0000-0000-0001-000000000003',
     'imaging', 'Chẩn đoán hình ảnh', 'ScanLine',
     'DICOM viewer, danh sách chờ đọc phim, TAT radiology.',
     ARRAY['doctor', 'admin'], 20),

    ('00000000-0000-0000-0002-000000000012',
     '00000000-0000-0000-0001-000000000003',
     'pharmacy', 'Dược', 'Pill',
     'Tồn kho thuốc, cảnh báo hết hạn, cấp phát, tương tác thuốc.',
     ARRAY['doctor', 'nurse', 'admin'], 30);

-- ── Group 4: Quản lý bệnh viện ───────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000013',
     '00000000-0000-0000-0001-000000000004',
     'bed-management', 'Quản lý giường', 'LayoutGrid',
     'Bản đồ giường toàn viện, chuyển khoa, dự báo trống giường.',
     ARRAY['nurse', 'admin'], 10),

    ('00000000-0000-0000-0002-000000000014',
     '00000000-0000-0000-0001-000000000004',
     'staff-management', 'Nhân sự', 'Users',
     'Lịch trực, tải công việc nhân viên, năng suất theo khoa.',
     ARRAY['admin'], 20),

    ('00000000-0000-0000-0002-000000000015',
     '00000000-0000-0000-0001-000000000004',
     'equipment', 'Trang thiết bị', 'Wrench',
     'Bảo trì thiết bị y tế, calibration, uptime, vị trí tài sản.',
     ARRAY['admin'], 30),

    ('00000000-0000-0000-0002-000000000016',
     '00000000-0000-0000-0001-000000000004',
     'finance', 'Tài chính', 'DollarSign',
     'Doanh thu, chi phí, công nợ, báo cáo tài chính bệnh viện.',
     ARRAY['admin'], 40),

    ('00000000-0000-0000-0002-000000000017',
     '00000000-0000-0000-0001-000000000004',
     'supply-chain', 'Hậu cần & Vật tư', 'PackageOpen',
     'Tồn kho vật tư y tế, đặt hàng, nhà cung cấp, tiêu hao.',
     ARRAY['admin'], 50);

-- ── Group 5: AI & Phân tích ───────────────────────────────────────────────────

INSERT INTO modules (id, group_id, slug, label, icon, description, required_roles, sort_order) VALUES
    ('00000000-0000-0000-0002-000000000018',
     '00000000-0000-0000-0001-000000000005',
     'population-health', 'Sức khỏe Cộng đồng', 'Globe',
     'Phân tầng nguy cơ, quản lý bệnh mãn tính, đăng ký bệnh nhân.',
     ARRAY['doctor', 'admin'], 10),

    ('00000000-0000-0000-0002-000000000019',
     '00000000-0000-0000-0001-000000000005',
     'predictive', 'Phân tích Dự báo', 'TrendingUp',
     'Dự báo nhập viện, readmission risk, dự báo cần giường.',
     ARRAY['admin'], 20),

    ('00000000-0000-0000-0002-000000000020',
     '00000000-0000-0000-0001-000000000005',
     'ai-assistant', 'Trợ lý AI', 'Bot',
     'AI chatbot y tế: trả lời câu hỏi về dữ liệu bệnh viện real-time.',
     NULL, 30),

    ('00000000-0000-0000-0002-000000000021',
     '00000000-0000-0000-0001-000000000005',
     'report-builder', 'Report Builder', 'PenLine',
     'Thiết kế báo cáo tuỳ chỉnh từ drag-drop Dashboard Designer.',
     ARRAY['admin'], 40)
ON CONFLICT (slug) DO NOTHING;
