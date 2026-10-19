namespace BMS.Api.Authorization;

/// <summary>Tên permission (phải khớp mảng "permissions" trong collection roles). Role có "ALL" qua mọi kiểm tra.</summary>
public static class Permissions
{
    public const string ManageServicePackage = "MANAGE_SERVICE_PACKAGE";
    public const string RegisterPatients = "REGISTER_PATIENTS,CREATE_APPOINTMENTS,MANAGE_APPOINTMENT,CHECK_IN,CHECKIN_PATIENT";
    public const string ViewPatientRecords = "VIEW_PATIENT_RECORDS,VIEW_PATIENTS,CLINIC_EXAM,REGISTER_PATIENTS";
    public const string ViewPatients = "VIEW_PATIENT_RECORDS,VIEW_PATIENTS,CLINIC_EXAM,REGISTER_PATIENTS";
    public const string ManageAppointment = "CREATE_APPOINTMENTS,MANAGE_APPOINTMENT,MANAGE_QUEUE";
    public const string CheckIn = "CHECKIN_PATIENT,CHECK_IN,REGISTER_PATIENTS";
    public const string ViewOwnProfile = "VIEW_OWN_PROFILE";
    public const string ManageSchedule = "MANAGE_SCHEDULE,MANAGE_LAB_WORKFLOW,CREATE_APPOINTMENTS,MANAGE_QUEUE";
    public const string ManageShifts = "MANAGE_SCHEDULE,MANAGE_LAB_WORKFLOW,CREATE_APPOINTMENTS,MANAGE_QUEUE";
    public const string ViewShifts = "VIEW_SCHEDULE,MANAGE_SCHEDULE,CLINIC_EXAM,REGISTER_PATIENTS,COLLECT_SAMPLES,RECEIVE_SAMPLES,MANAGE_LAB_WORKFLOW";
    public const string CollectSample = "COLLECT_SAMPLES,COLLECT_SAMPLE,SCAN_RFID,PRINT_BARCODE";
    public const string ReceiveSample = "RECEIVE_SAMPLES,RECEIVE_SAMPLE";
    public const string RunAnalyzer = "PROCESS_TESTS,RUN_ANALYZER,CALIBRATE_DEVICES,RUN_QC";
    public const string EnterResult = "INPUT_RESULTS,ENTER_RESULT,PROCESS_TESTS";
    public const string ApproveResult = "APPROVE_RESULTS,APPROVE_RESULT,SIGN_REPORTS";
    public const string ViewAllLab = "MANAGE_LAB_WORKFLOW,VIEW_ALL_LAB,REJECT_SPECIMEN,REQUEST_RERUN";
    public const string RecordSampleTime = "COLLECT_SAMPLES,RECEIVE_SAMPLES,MANAGE_LAB_WORKFLOW,MANAGE_LAB_QUEUE";
    public const string UpdateSampleStatus = "COLLECT_SAMPLES,RECEIVE_SAMPLES,PROCESS_TESTS,REJECT_SPECIMEN,REQUEST_RERUN,MANAGE_LAB_WORKFLOW,MANAGE_LAB_QUEUE";
}

