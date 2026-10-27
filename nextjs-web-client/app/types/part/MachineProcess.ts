import { MachineProcessType } from "../common/MachineProcessType";
import { ProcessStatus } from "../common/ProcessStatus";
import { PartType } from "./PartType";

export interface MachineProcess {
  id: number;
  createAt: string;
  createBy: string;
  updateAt?: string | null;
  updateBy?: string | null;
  partId: number;
  partCode: string;
  quantity: number;
  status: ProcessStatus;
  note: string;
  plantDate: string;
  startTime?: string | null;
  endTime?: string | null;
  workingTime?: number | null;
  sequence: number;
  type: MachineProcessType;
  prioritize: boolean;
  isClosed: boolean;
  estimatedTime?: number | null;
  partType?: PartType | null;
  isOverdue: boolean | null;
  isCutoverCompleted: boolean;
  indexCode: string | null;
  referenceId: number | null;
  processedQuantity: number | null;
  totalProcessedQuantity: number;
  donePreviouslyQuantity: number | null;
}
