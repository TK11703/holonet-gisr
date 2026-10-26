# Data Model

## Account
Nullable `operating_number varchar(64)`; directory refresh only. All account projections include it. AccountDto appends optional OperatingNumber to preserve constructors.

## Chain Code Profile
`account_chain_code`: account_id UUID primary key / FK account(id) ON DELETE CASCADE; profile JSONB NOT NULL; chain_code varchar(47) NOT NULL; updated_at timestamptz NOT NULL.

FullName, BirthDate, Homeworld, Species: nullable strings maximum 200 each. Height: nullable string maximum 80. EyeColor, SkinColor, HairColor: nullable enum. DistinguishingMarks: nullable string maximum 1000. FamilyHistory, CriminalHistory, Biography: nullable strings maximum 4000 each. At least one meaningful field required; fields individually optional.

Colors: Black, Brown, Blue, Green, Gray, Hazel, Amber, Red, Orange, Yellow, Gold, Silver, White, Pink, Purple, Tan, Pale, Dark, None, Other. Undefined enum values rejected. Optional EyeColors, SkinColors, and HairColors arrays allow up to 20 values each, presented as removable tags through an Add color selector. Scalars remain for backward compatibility. Normalize the union of scalar and array values into sorted distinct sets: no selections becomes null, one becomes the scalar, and multiple become the array with a null scalar. Omit null arrays from serialized profiles to preserve legacy hash payloads. The existing JSONB column stores the added arrays without a migration.

Absent -> saved -> updated -> absent on deletion. Meaningful edits regenerate code. All queries scope by authenticated account_id. Missing GET/DELETE returns 404 ProblemDetails; PUT creates/replaces with 200.